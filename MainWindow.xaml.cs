//------------------------------------------------------------------------------
// <copyright file="MainWindow.xaml.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------
namespace Emotifier
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Windows;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;
    using Microsoft.Kinect;
    using System.Collections.Generic;
    using Microsoft.ProjectOxford.Emotion;
    using Microsoft.ProjectOxford.Emotion.Contract;
    using System.Drawing.Imaging;
    using System.Drawing;
    using System.Linq;
    using System.Xml.Serialization;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Interaction logic for MainWindow
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly string[] EmoStrings = { "Anger", "Disgust", "Sadness", "X", "Joy", "Fear" };
        private enum Emotions
        {
            anger,
            contempt,
            sadness,
            neutral,
            happiness,
            fear
        }
        #region Image processing fields
        /// <summary>
        /// Active Kinect sensor
        /// </summary>
        private KinectSensor kinectSensor = null;
        /// <summary>
        /// Coordinate mapper to map one type of point to another
        /// </summary>
        private CoordinateMapper coordinateMapper = null;
        /// <summary>
        /// Reader for body frames
        /// </summary>
        private BodyFrameReader bodyFrameReader = null;

        /// <summary>
        /// Constant for clamping Z values of camera space points from being negative
        /// </summary>
        private const float InferredZPositionClamp = 0.1f;

        /// <summary>
        /// Reader for color frames
        /// </summary>
        private ColorFrameReader colorFrameReader = null;

        /// <summary>
        /// Bitmap to display
        /// </summary>
        private WriteableBitmap colorBitmap = null;
        private Emotion[] detectedEmotions = null;
        private List<Tuple<ColorSpacePoint, float, ulong>> headPositions = new List<Tuple<ColorSpacePoint, float, ulong>>();
        private EmotionServiceClient emotionServiceClient;
        private Bitmap myBitmap;
        private Stopwatch stopwatchEmotionUpate = new Stopwatch();
        private int frameCnt = 0;
        private ApiCredentials apiCredentials;
        #endregion

        #region Audio processing fields
        /// <summary>
        /// Number of bytes in each Kinect audio stream sample (32-bit IEEE float).
        /// </summary>
        private const int BytesPerAudioSample = sizeof(float);
        /// <summary>
        /// Will be allocated a buffer to hold a single sub frame of audio data read from audio stream.
        /// </summary>
        private readonly byte[] audioBuffer = null;
        /// <summary>
        /// Reader for audio frames
        /// </summary>
        private AudioBeamFrameReader audioBeamReader = null;
        //private List<short> audioSamples;
        private MemoryStream audioSnippet;
        private int maxAudioSamples;
        private SpeechToEmotionClient speechToEmotionClient;
        private Stopwatch stopwatchSpeechBubble = new Stopwatch();
        private Bitmap speechBubble = null;
        #endregion

        #region Constructor
        /// <summary>
        /// Initializes a new instance of the MainWindow class.
        /// </summary>
        public MainWindow()
        {
            // Load credentials
            XmlSerializer xmlSerializer = new XmlSerializer(typeof(ApiCredentials));
            if (!File.Exists("ApiCredentials.xml"))
            {
                MessageBox.Show("Please place file ApiCredentials.xml in your execution directory. See README for more instructions.");
                Application.Current.Shutdown();
            }

            using (StreamReader sr = new StreamReader("ApiCredentials.xml"))
            {
                apiCredentials = (ApiCredentials)xmlSerializer.Deserialize(sr);
            }
            emotionServiceClient = new EmotionServiceClient(apiCredentials.EmotionAPIKey);
            speechToEmotionClient = new SpeechToEmotionClient(apiCredentials.WatsonPassword, apiCredentials.WatsonUsername, apiCredentials.BingSubscriptionKey);

            speechToEmotionClient.TextEmotionRecognized += s2e_TextEmotionRecognized;
            stopwatchEmotionUpate.Start();
            // get the kinectSensor object
            this.kinectSensor = KinectSensor.GetDefault();

            // open the reader for the color frames
            this.colorFrameReader = this.kinectSensor.ColorFrameSource.OpenReader();

            // wire handler for frame arrival
            this.colorFrameReader.FrameArrived += this.Reader_ColorFrameArrived;
            this.bodyFrameReader = this.kinectSensor.BodyFrameSource.OpenReader();
            this.bodyFrameReader.FrameArrived += this.BodyFrameReader_FrameArrived;

            // get the coordinate mapper
            this.coordinateMapper = this.kinectSensor.CoordinateMapper;
            // open the reader for the body frames
            this.bodyFrameReader = this.kinectSensor.BodyFrameSource.OpenReader();

            // create the colorFrameDescription from the ColorFrameSource using Bgra format
            FrameDescription colorFrameDescription = this.kinectSensor.ColorFrameSource.CreateFrameDescription(ColorImageFormat.Bgra);

            // create the bitmap to display
            this.colorBitmap = new WriteableBitmap(colorFrameDescription.Width, colorFrameDescription.Height, 96.0, 96.0, PixelFormats.Bgr32, null);

            // set IsAvailableChanged event notifier
            this.kinectSensor.IsAvailableChanged += this.Sensor_IsAvailableChanged;

            // open the sensor
            this.kinectSensor.Open();

            // set the status text
            this.StatusText = this.kinectSensor.IsAvailable ? Properties.Resources.RunningStatusText : Properties.Resources.NoSensorStatusText;

            // use the window object as the view model in this simple example
            this.DataContext = this;

            // Get its audio source
            AudioSource audioSource = this.kinectSensor.AudioSource;

            // Allocate 1024 bytes to hold a single audio sub frame. Duration sub frame 
            // is 16 msec, the sample rate is 16khz, which means 256 samples per sub frame. 
            // With 4 bytes per sample, that gives us 1024 bytes.
            this.audioBuffer = new byte[audioSource.SubFrameLengthInBytes];
            this.maxAudioSamples = (int)((audioSource.SubFrameLengthInBytes / sizeof(float)) * 512);
            //this.audioSamples = new List<short>(maxAudioSamples);
            this.audioSnippet = new MemoryStream(maxAudioSamples * 2);
            // Open the reader for the audio frames
            this.audioBeamReader = audioSource.OpenReader();
            this.audioBeamReader.FrameArrived += this.Reader_AudioFrameArrived;
            // initialize the components (controls) of the window
            this.InitializeComponent();
        }

        void s2e_TextEmotionRecognized(object sender, SpeechToEmotionClient.TextEmotionRecognizedEventArgs e)
        {
            if (e.Emotions == null)
            {
                return;
            }
            List<KeyValuePair<string, double>> emoList = e.Emotions.ToList();
            emoList.Sort((pair1, pair2) => pair2.Value.CompareTo(pair1.Value));
            List<Emotions> emos = new List<Emotions>();
            for (int i = 0; i < 3; i++)
            {
                if (emoList[i].Value < 0.3)
                {
                    break;
                }
                int j = Array.IndexOf<string>(EmoStrings, emoList[i].Key);
                emos.Add((Emotions)j);
            }
            if (emos.Count == 0)
            {
                emos.Add(Emotions.neutral);
            }
            SetSpeechBubble(emos.ToArray());
        }
        #endregion

        #region Image Processing Methods
        private void Emotify(Stream image)
        {
            System.Threading.Tasks.Task<Emotion[]> task = emotionServiceClient.RecognizeAsync(image);
            task.ContinueWith((System.Threading.Tasks.Task<Emotion[]> t) => (detectedEmotions = t.Result));
        }

        /// <summary>
        /// Handles the body frame data arriving from the sensor
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void BodyFrameReader_FrameArrived(object sender, BodyFrameArrivedEventArgs e)
        {
            headPositions.Clear();
            bool dataReceived = false;
            Body[] bodies = null;
            using (BodyFrame bodyFrame = e.FrameReference.AcquireFrame())
            {
                if (bodyFrame != null)
                {
                    if (bodies == null)
                    {
                        bodies = new Body[bodyFrame.BodyCount];
                    }

                    // The first time GetAndRefreshBodyData is called, Kinect will allocate each Body in the array.
                    // As long as those body objects are not disposed and not set to null in the array,
                    // those body objects will be re-used.
                    bodyFrame.GetAndRefreshBodyData(bodies);
                    dataReceived = true;
                }
            }

            if (dataReceived)
            {
                foreach (Body body in bodies)
                {
                    if (body.IsTracked)
                    {
                        IReadOnlyDictionary<JointType, Joint> joints = body.Joints;

                        // convert the joint points to depth (display) space
                        Dictionary<JointType, System.Windows.Point> jointPoints = new Dictionary<JointType, System.Windows.Point>();
                        {
                            // sometimes the depth(Z) of an inferred joint may show as negative
                            // clamp down to 0.1f to prevent coordinatemapper from returning (-Infinity, -Infinity)
                            CameraSpacePoint position = joints[JointType.Head].Position;
                            if (position.Z < 0)
                            {
                                position.Z = InferredZPositionClamp;
                            }
                            ColorSpacePoint[] colorSpacePoint = new ColorSpacePoint[1];
                            this.coordinateMapper.MapCameraPointsToColorSpace(new CameraSpacePoint[] { position }, colorSpacePoint);
                            // Cut and emotify!
                            headPositions.Add(new Tuple<ColorSpacePoint, float, ulong>(colorSpacePoint[0], position.Z, body.TrackingId));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Gets the bitmap to display
        /// </summary>
        public ImageSource ImageSource
        {
            get
            {
                return this.colorBitmap;
            }
        }

        /// <summary>
        /// Handles the color frame data arriving from the sensor
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void Reader_ColorFrameArrived(object sender, ColorFrameArrivedEventArgs e)
        {
            frameCnt++;

            // ColorFrame is IDisposable
            using (ColorFrame colorFrame = e.FrameReference.AcquireFrame())
            {
                if (colorFrame != null)
                {
                    FrameDescription colorFrameDescription = colorFrame.FrameDescription;

                    using (KinectBuffer colorBuffer = colorFrame.LockRawImageBuffer())
                    {
                        if (myBitmap == null || colorFrameDescription.Width != myBitmap.Width || colorFrameDescription.Height != myBitmap.Height)
                        {
                            myBitmap = new Bitmap(colorFrameDescription.Width, colorFrameDescription.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        }
                        BitmapData bmpData = myBitmap.LockBits(new Rectangle(0,0, myBitmap.Width, myBitmap.Height), ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        colorFrame.CopyConvertedFrameDataToIntPtr(bmpData.Scan0,
                                (uint)(colorFrameDescription.Width * colorFrameDescription.Height * 4),
                                ColorImageFormat.Bgra);
                        
                        myBitmap.UnlockBits(bmpData);

                        Graphics graphics = Graphics.FromImage(myBitmap);
                        if (headPositions != null && headPositions.Count != 0)
                        {
                            if (stopwatchEmotionUpate.ElapsedMilliseconds > 3000)
                            {
                                MemoryStream ms = new MemoryStream();
                                myBitmap.Save(ms, ImageFormat.Jpeg);
                                ms.Seek(0, SeekOrigin.Begin);
                                try
                                {
                                    Emotify(ms);
                                }
                                catch (Exception)
                                {
                                }
                                stopwatchEmotionUpate.Restart();
                            }

                            System.Drawing.PointF[] emoHeadPositions = null;
                            if (detectedEmotions != null)
                            {
                                emoHeadPositions = new System.Drawing.PointF[detectedEmotions.Length];

                                for (int i = 0; i < detectedEmotions.Length; i++)
                                {
                                    emoHeadPositions[i] = new System.Drawing.PointF(detectedEmotions[i].FaceRectangle.Left + detectedEmotions[i].FaceRectangle.Width / 2.0f, detectedEmotions[i].FaceRectangle.Top + detectedEmotions[i].FaceRectangle.Height / 2.0f);
                                }
                            }
                            foreach (var posDepth in headPositions)
	                        {
                                ColorSpacePoint pos = posDepth.Item1;
                                float depth = posDepth.Item2;
                                float depthFactor = 5.0f / depth;
                                Bitmap emotionBitmap = Properties.Resources.anger;
                                if (detectedEmotions != null && detectedEmotions.Length > 0)
                                {
                                    int headCount = -1;
                                    float minLength = float.MaxValue;
                                    for (int i = 0; i < detectedEmotions.Length; i++)
                                    {
                                        System.Drawing.PointF diff = new System.Drawing.PointF(emoHeadPositions[i].X - pos.X, emoHeadPositions[i].Y - pos.Y);
                                        float diffX = emoHeadPositions[i].X - pos.X;
                                        float diffY = emoHeadPositions[i].Y - pos.Y;
                                        float length = (float) Math.Sqrt(diffX * diffX + diffY * diffY);
                                        if (length < minLength)
                                        {
                                            headCount = i;
                                            minLength = length;
                                        }
                                    }
                                    if (headCount < 0)
                                    {
                                        headCount = 0;
                                    }

                                    foreach (KeyValuePair<string, float> emo in detectedEmotions[headCount].Scores.ToRankedList())
                                    {
                                        switch (emo.Key.ToLower())
                                        {
                                            case "anger":
                                                emotionBitmap = Properties.Resources.anger;
                                                break;
                                            case "contempt":
                                                emotionBitmap = Properties.Resources.contempt;
                                                break;
                                            case "sadness":
                                                emotionBitmap = Properties.Resources.sadness;
                                                break;
                                            case "neutral":
                                                emotionBitmap = Properties.Resources.neutral;
                                                break;
                                            case "happiness":
                                                emotionBitmap = Properties.Resources.happiness;
                                                break;
                                            case "fear":
                                                emotionBitmap = Properties.Resources.fear;
                                                break;
                                            default:
                                                emotionBitmap = Properties.Resources.neutral;
                                                break;
                                        }
                                        break;
                                    }
                                }
                                if (pos.X < -20 || pos.X > myBitmap.Width + 20 || pos.Y < -20 || pos.Y > myBitmap.Height + 20)
                                {
                                    continue;
                                }
                                graphics.DrawImage(emotionBitmap, new RectangleF(pos.X - emotionBitmap.Width * depthFactor / 2, pos.Y - emotionBitmap.Height * depthFactor / 2, emotionBitmap.Width * depthFactor, emotionBitmap.Height * depthFactor), new RectangleF(0,0,emotionBitmap.Width, emotionBitmap.Height), GraphicsUnit.Pixel);
                            }

                        }
                        if (speechBubble != null)
                        {
                            //float depth = headPositions[0].Item2;
                            //float depthFactor = 5.0f / depth;
                            //graphics.DrawImage(speechBubble, new Rectangle((int)(headPositions[0].Item1.X), (int)(headPositions[0].Item1.Y - depthFactor * 200), speechBubble.Width, speechBubble.Height));
                            graphics.DrawImage(speechBubble, new Rectangle(10, 10, speechBubble.Width * 2, speechBubble.Height * 2));
                            if (stopwatchSpeechBubble.ElapsedMilliseconds > 5000)
                            {
                                stopwatchSpeechBubble.Stop();
                                speechBubble = null;
                            }
                        }

                        this.colorBitmap.Lock();

                        // verify data and write the new color frame data to the display bitmap
                        if ((colorFrameDescription.Width == this.colorBitmap.PixelWidth) && (colorFrameDescription.Height == this.colorBitmap.PixelHeight))
                        {
                            bmpData = myBitmap.LockBits(new Rectangle(0,0, myBitmap.Width, myBitmap.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                            CopyMemory(this.colorBitmap.BackBuffer, bmpData.Scan0, new IntPtr(bmpData.Stride * bmpData.Height));

                            myBitmap.UnlockBits(bmpData);
                            this.colorBitmap.AddDirtyRect(new Int32Rect(0, 0, this.colorBitmap.PixelWidth, this.colorBitmap.PixelHeight));
                        }

                        this.colorBitmap.Unlock();
                    }
                }
            }
        }

        /// <summary>
        /// Copies a block of unmanaged memory from one location to another. (from MSDN)
        /// </summary>
        /// <param name="destination">A pointer to the starting address of the copied block's destination. (from MSDN)</param>
        /// <param name="source">A pointer to the starting address of the block of memory to copy. (from MSDN)</param>
        /// <param name="sizeInBytes">The size of the block of memory to copy, in bytes. (from MSDN)</param>
        /// <remarks>
        /// This actually uses MoveMemory, because CopyMemory has undefined results if the source and destination blocks overlap.
        /// </remarks>
        [DllImport("Kernel32.dll", EntryPoint = "RtlMoveMemory", SetLastError = false)]
        public static extern void CopyMemory(IntPtr destination, IntPtr source, IntPtr sizeInBytes);

        #endregion

        #region Audio processing methods
        private void SetSpeechBubble(Emotions[] emo)
        {
            if (speechBubble != null)
            {
                return;
            }
            Bitmap speechBub = Properties.Resources.speech_baloon;
            Graphics graphics = Graphics.FromImage(speechBub);
            
            for (int i = 0; i < emo.Length; i++)
            {
                Rectangle targetRect = new Rectangle(45 + 70 * i, 70, 70, 70);
                switch (emo[i])
                {
                    case Emotions.contempt:
                        graphics.DrawImage(Properties.Resources.contempt, targetRect);
                        break;
                    case Emotions.sadness:
                        graphics.DrawImage(Properties.Resources.sadness, targetRect);
                        break;
                    case Emotions.neutral:
                        graphics.DrawImage(Properties.Resources.neutral, targetRect);
                        break;
                    case Emotions.happiness:
                        graphics.DrawImage(Properties.Resources.happiness, targetRect);
                        break;
                    case Emotions.fear:
                        graphics.DrawImage(Properties.Resources.fear, targetRect);
                        break;
                    default:
                        break;
                }
            }
            speechBubble = speechBub;
            stopwatchSpeechBubble.Restart();
        }
        /// <summary>
        /// Handles the audio frame data arriving from the sensor
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void Reader_AudioFrameArrived(object sender, AudioBeamFrameArrivedEventArgs e)
        {
            AudioBeamFrameReference frameReference = e.FrameReference;
            AudioBeamFrameList frameList = frameReference.AcquireBeamFrames();

            if (frameList != null)
            {
                BinaryWriter bw = new BinaryWriter(audioSnippet, System.Text.Encoding.ASCII, true);
                // AudioBeamFrameList is IDisposable
                using (frameList)
                {
                    // Only one audio beam is supported. Get the sub frame list for this beam
                    IReadOnlyList<AudioBeamSubFrame> subFrameList = frameList[0].SubFrames;

                    // Loop over all sub frames, extract audio buffer and beam information
                    foreach (AudioBeamSubFrame subFrame in subFrameList)
                    {
                        // Process audio buffer
                        subFrame.CopyFrameDataToArray(this.audioBuffer);
                        for (int i = 0; i < this.audioBuffer.Length; i += BytesPerAudioSample)
                        {
                            // Extract the 32-bit IEEE float sample from the byte array
                            float audioSample = BitConverter.ToSingle(this.audioBuffer, i);
                            //audioSamples.Add((short)(audioSample * short.MaxValue));
                            bw.Write((short)(audioSample * short.MaxValue));
                        }
                        if (audioSnippet.Position >= maxAudioSamples)
                        {
                            // Send over data
                            speechToEmotionClient.SendBytes(audioSnippet.GetBuffer());
                            audioSnippet.Seek(0, SeekOrigin.Begin);
                        }
                    }
                }
                bw.Close();
            }
        }
        #endregion

        #region General stuff and GUI
        /// <summary>
        /// INotifyPropertyChangedPropertyChanged event to allow window controls to bind to changeable data
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;
        /// <summary>
        /// Current status text to display
        /// </summary>
        private string statusText = null;
        /// <summary>
        /// Handles the event which the sensor becomes unavailable (E.g. paused, closed, unplugged).
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void Sensor_IsAvailableChanged(object sender, IsAvailableChangedEventArgs e)
        {
            // on failure, set the status text
            this.StatusText = this.kinectSensor.IsAvailable ? Properties.Resources.RunningStatusText
                                                            : Properties.Resources.SensorNotAvailableStatusText;
        }
        /// <summary>
        /// Gets or sets the current status text to display
        /// </summary>
        public string StatusText
        {
            get
            {
                return this.statusText;
            }

            set
            {
                if (this.statusText != value)
                {
                    this.statusText = value;

                    // notify any bound elements that the text has changed
                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new PropertyChangedEventArgs("StatusText"));
                    }
                }
            }
        }
        /// <summary>
        /// Execute shutdown tasks
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (this.colorFrameReader != null)
            {
                // ColorFrameReder is IDisposable
                this.colorFrameReader.Dispose();
                this.colorFrameReader = null;
            }

            if (this.bodyFrameReader != null)
            {
                this.bodyFrameReader.Dispose();
                this.bodyFrameReader = null;
            }

            if (this.audioBeamReader != null)
            {
                this.audioBeamReader.Dispose();
                this.audioBeamReader = null;
            }

            if (this.kinectSensor != null)
            {
                this.kinectSensor.Close();
                this.kinectSensor = null;
            }
        }
        #endregion
    }
}
