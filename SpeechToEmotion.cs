using Microsoft.Bing.Speech;
using Microsoft.CognitiveServices.SpeechRecognition;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Speech2Emotion
{
    public class SpeechToEmotion
    {
        public class TextEmotionRecognizedEventArgs
        {
            public TextEmotionRecognizedEventArgs(Dictionary<string, double> emotions)
            {
                this.Emotions = emotions;
            }

            public Dictionary<string, double> Emotions { get; private set; }
        }
        public delegate void TextEmotionHandler(object sender, TextEmotionRecognizedEventArgs e);
        public event TextEmotionHandler TextEmotionRecognized;

        private string BingSpeechSubscriptionKey;

        private static readonly string WatsonUrl = "https://gateway.watsonplatform.net/tone-analyzer/api";
        private static readonly string WatsonVersion = "v3/tone?version=2016-05-19";
        private string WatsonPassword;
        private string WatsonUsername;

        private DataRecognitionClient dataRecognitionClient;
        private MicrophoneRecognitionClient microphonoRegocnitionClient;
        public static readonly string Language = "en-US";

        public SpeechToEmotion(string watsonPassword, string watsonUsername, string bingSubscriptionKey)
        {
            BingSpeechSubscriptionKey = bingSubscriptionKey;
            WatsonPassword = watsonPassword;
            WatsonUsername = watsonUsername;
            dataRecognitionClient = SpeechRecognitionServiceFactory.CreateDataClient(SpeechRecognitionMode.ShortPhrase, Language, BingSpeechSubscriptionKey);
            dataRecognitionClient.OnResponseReceived += DataRecognitionClient_OnResponseReceived;
            dataRecognitionClient.OnConversationError += DataRecognitionClient_OnConversationError;

            SpeechAudioFormat af = new SpeechAudioFormat();
            af.AverageBytesPerSecond = 16000 * 2;
            af.BitsPerSample = 16;
            af.ChannelCount = 1;
            af.SamplesPerSecond = 16000;
            af.EncodingFormat = AudioCompressionType.PCM;
            dataRecognitionClient.SendAudioFormat(af);

            microphonoRegocnitionClient = SpeechRecognitionServiceFactory.CreateMicrophoneClient(SpeechRecognitionMode.ShortPhrase, Language, BingSpeechSubscriptionKey);
            microphonoRegocnitionClient.OnResponseReceived += MicrophonoRegocnitionClient_OnResponseReceived;
            microphonoRegocnitionClient.OnConversationError += MicrophonoRegocnitionClient_OnConversationError;
        }

        private void DataRecognitionClient_OnConversationError(object sender, SpeechErrorEventArgs e)
        {
            Console.Error.WriteLine("Conversation error ocurred");
        }

        private void DataRecognitionClient_OnResponseReceived(object sender, SpeechResponseEventArgs e)
        {
            if (e.PhraseResponse.RecognitionStatus != Microsoft.CognitiveServices.SpeechRecognition.RecognitionStatus.RecognitionSuccess)
            {
                return;
            }
            Console.WriteLine(e.PhraseResponse.Results[0].DisplayText);
            if (TextEmotionRecognized != null)
            {
               TextEmotionRecognized(this, new  TextEmotionRecognizedEventArgs(AnalyzeTone(e.PhraseResponse.Results[0].DisplayText)));
            }
        }

        private void WriteResponseResult(SpeechResponseEventArgs e)
        {
            if (e.PhraseResponse.Results.Length == 0)
            {
                Console.WriteLine("No phrase response is available.");
            }
            else
            {
                Console.WriteLine("********* Final n-BEST Results *********");
                for (int i = 0; i < e.PhraseResponse.Results.Length; i++)
                {
                    Console.WriteLine(
                        "[{0}] Confidence={1}, Text=\"{2}\"",
                        i,
                        e.PhraseResponse.Results[i].Confidence,
                        e.PhraseResponse.Results[i].DisplayText);
                }

                Console.WriteLine();
            }
        }

        public void SendAudioFile(string filename)
        {
            using (FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                int bytesRead = 0;
                byte[] buffer = new byte[1024];

                try
                {
                    do
                    {
                        bytesRead = fs.Read(buffer, 0, buffer.Length);
                        dataRecognitionClient.SendAudio(buffer, bytesRead);
                    } while (bytesRead > 0);
                }
                finally
                {
                    dataRecognitionClient.EndAudio();
                }
            }
        }

        public void SendBytes(byte[] buffer)
        {
            try
            {
                dataRecognitionClient.SendAudio(buffer, buffer.Length);
            }
            finally
            {
                dataRecognitionClient.EndAudio();
            }
        }
        
        public void RecordFromMicrophone()
        {
            microphonoRegocnitionClient.StartMicAndRecognition();
        }

        private void MicrophonoRegocnitionClient_OnConversationError(object sender, SpeechErrorEventArgs e)
        {
            Console.WriteLine(e.ToString());
        }

        private void MicrophonoRegocnitionClient_OnResponseReceived(object sender, SpeechResponseEventArgs e)
        {
            Console.WriteLine(e.ToString());
        }

        public JObject Analyze(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            using (HttpClientHandler handler = new HttpClientHandler())
            {
                handler.Credentials = new NetworkCredential(WatsonUsername, WatsonPassword);
                using (HttpClient client = new HttpClient(handler))
                {
                    Dictionary<string, string> data = new Dictionary<string, string>();
                    data["text"] = text;
                    StringContent content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
                    string url = WatsonUrl + "/" + WatsonVersion;
                    HttpResponseMessage response = client.PostAsync(url, content).Result;
                    if (HttpStatusCode.OK != response.StatusCode)
                    {
                        return null;
                    }
                    string responseBody = response.Content.ReadAsStringAsync().Result;
                    return JObject.Parse(responseBody);
                }
            }
        }

        public Dictionary<string, double> AnalyzeTone(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            Dictionary<string, double> tones = new Dictionary<string, double>();
            JObject analysisResult = Analyze(text);
            JToken toneTokens = analysisResult["document_tone"]["tone_categories"][0]["tones"];
            foreach (JToken toneToken in toneTokens)
            {
                string toneName = toneToken["tone_name"].ToString();
                double score = Double.Parse(toneToken["score"].ToString());
                tones[toneName] = score;
            }

            return tones;
        }
    }
}
