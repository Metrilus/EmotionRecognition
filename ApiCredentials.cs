using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Emotifier
{
    public class ApiCredentials
    {
        public string WatsonPassword { get; set; }
        public string WatsonUsername { get; set; }
        public string EmotionAPIKey { get; set; }
        public string BingSubscriptionKey { get; set; }

        public ApiCredentials()
        {

        }
    }
}
