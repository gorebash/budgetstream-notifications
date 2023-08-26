using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebPush;

namespace BudgetStream.Notifications.Models
{
    public class User
    {
        [JsonProperty("id")]
        public string DocumentId { get; set; }

        [JsonProperty("pk")]
        public Pk PartitionKey { get; set; } = new Pk();

        [JsonProperty("subscription")]
        public PushSubscription NotificationSubscription { get; set; }

        [JsonProperty("fiKeys")]
        public List<FiKeys> FiKeys { get; set; } = new List<FiKeys>();
    }

    public class Pk
    {
        [JsonProperty("userId")]
        public string UserId { get; set; }
    }

    public class FiKeys
    {
        public string AccessToken { get; set; }
        public string ItemId { get; set; }
        public string Cursor { get; set; }
    }
}
