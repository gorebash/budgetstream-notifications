using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using WebPush;
using System.Linq;

namespace BudgetStream.Notifications.Functions
{
    public static class Subscribe
    {
        // todo: temp memory store -- replace with saving to db.
        private static List<PushSubscription> _subs = new List<PushSubscription>();


        [FunctionName(nameof(AddSubscriber))]
        public static async Task<IActionResult> AddSubscriber(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic rawBody = JsonConvert.DeserializeObject(requestBody);
            var sub = MapFrom(rawBody);

            // todo: bail if sub aready exists.

            // temp limiter for POC, eventually replace with db.
            if (_subs.Count > 10)
                throw new Exception("Subscriber queue is full.");

            _subs.Add(sub);

            return new OkResult();
        }


        [FunctionName(nameof(SendNotifications))]
        public static async Task<IActionResult> SendNotifications(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            // todo: move to injected
            var pushClient = new WebPushClient();

            // todo: only needed for e2e testing.
            var message = await new StreamReader(req.Body).ReadToEndAsync();

            var privateKey = Environment.GetEnvironmentVariable("VAPID_PRIVATE_KEY");
            if (string.IsNullOrEmpty(privateKey))
                throw new ArgumentNullException("VAPID_PRIVATE_KEY", "VAPID private key not set.");

            var publicKey = Environment.GetEnvironmentVariable("VAPID_PUBLIC_KEY");
            if (string.IsNullOrEmpty(publicKey))
                throw new ArgumentNullException("VAPID_PRIVATE_KEY", "VAPID public key not set.");

            var subject = Environment.GetEnvironmentVariable("VAPID_SUBJECT");
            if (string.IsNullOrEmpty(subject))
                throw new ArgumentNullException("VAPID_SUBJECT", "VAPID subject not set.");

            var keys = new VapidDetails
            {
                PrivateKey = privateKey,
                PublicKey = publicKey,
                Subject = subject
            };

            _subs.ForEach(async sub => 
                await pushClient.SendNotificationAsync(sub, message, keys));

            return new OkResult();
        }


        private static PushSubscription MapFrom(dynamic postBody) =>
            new PushSubscription
            {
                Endpoint = postBody.endpoint,
                Auth = postBody.keys.auth,
                P256DH = postBody.keys.p256dh
            };
    }
}
