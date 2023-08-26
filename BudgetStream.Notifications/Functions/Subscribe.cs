using BudgetStream.Notifications.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using WebPush;

namespace BudgetStream.Notifications.Functions
{
    public static class Subscribe
    {
        // Temp memory store -- eventually replace with saving to db.
        private static List<PushSubscription> _subs = new List<PushSubscription>();


        [FunctionName(nameof(AddSubscriber))]
        public static async Task<IActionResult> AddSubscriber(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            [CosmosDB(
                databaseName: "BudgetStream",
                collectionName: "Users",
                ConnectionStringSetting = "UserStore.ConnectionString")]IAsyncCollector<User> userDocumentOut,
            ILogger log)
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic payload = JsonConvert.DeserializeObject(requestBody);
            
            // bind subscription properties
            var sub = MapFrom(payload.subscription);

            // bind user document properties
            var user = JsonConvert.DeserializeObject<User>(requestBody);
            user.NotificationSubscription = sub;

            // By saving locally it enables test notifications until the notifications trigger also pulls from cosmos.
            _subs.Add(sub);


            await userDocumentOut.AddAsync(user);
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

        /// <summary>
        /// Transactions are enqueued from Plaid webhook. The function will trigger and send notifications.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="log"></param>
        [FunctionName(nameof(SendNotifications))] // rename to transactionAdded
        public static void SendNotifications(
            [QueueTrigger("transactions-queue", Connection = "TransactionsQueue.ConnectionString")] string transaction,
            ILogger log)
        {
            // todo: retrieve user document from cosmos musing transaction detail.
            // need to write the sql binding to tie transaction to document using the subscription body.

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

            // todo: add user filtering

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
