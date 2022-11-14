using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using WebPush;
using System.Linq;
using BudgetStream.Notifications.Models;
using System.Text.Json;
using Newtonsoft.Json;
using System.Transactions;

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

            // todo: convert to JsonSerializer async.
            dynamic rawBody = JsonConvert.DeserializeObject(requestBody);

            var sub = MapFrom(rawBody);

            // todo: bail if sub aready exists.

            // temp limiter for POC, eventually replace with db.
            if (_subs.Count > 10)
                throw new Exception("Subscriber queue is full.");

            _subs.Add(sub);

            return new OkResult();
        }


        /**
         * Http trigger to manually invoke notifications.
         * For testing only.
         */
        [FunctionName(nameof(TriggerNotifications))]
        public static async Task<IActionResult> TriggerNotifications (
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation($"{nameof(SendNotifications)} queue triggered with {_subs.Count} subscribers.");

            var transaction = await new StreamReader(req.Body).ReadToEndAsync();
            SendPushNotification(transaction);

            return new OkResult();
        }


        [FunctionName(nameof(SendNotifications))] // rename to transactionAdded
        public static void SendNotifications(
            [QueueTrigger("transactions-queue", Connection = "TransactionsQueue.ConnectionString")] string transaction,
            ILogger log)
        {
            log.LogInformation($"{nameof(SendNotifications)} queue triggered with {_subs.Count} subscribers.");

            SendPushNotification(transaction);
        }


        private static PushSubscription MapFrom(dynamic postBody) =>
            new PushSubscription
            {
                Endpoint = postBody.endpoint,
                Auth = postBody.keys.auth,
                P256DH = postBody.keys.p256dh
            };


        private static void SendPushNotification (string transaction)
        {
            // todo: move to injected
            var pushClient = new WebPushClient();

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

            /* todo: shape notification data using service worker sdk:
            {
                "notification": {
                    "title": "New transcation..",
                    "data": {
                        "onActionClick": {
                            "default": {"operation": "focusLastFocusedOrOpen"}
                        }
                    }
                }
            }
            */


            _subs.ForEach(async sub =>
                await pushClient.SendNotificationAsync(sub, transaction, keys));
        }
    }
}
