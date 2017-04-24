# Emotifier
This application showcases the usage of an emotion recognition engine together with the Kinect body tracking.

## Prerequisties
### REST APIs
Emotifier makes use of several REST-APIs you need to register for. Currently it uses Bing services, IBM Watson und the Microsoft Cognitive Services.

- Obtain your key of the Emotion API here: https://www.microsoft.com/cognitive-services/en-us/emotion-api
- The key for the IBM Watson API can be found here: https://www.ibm.com/watson/developercloud/

You need to register for these APIs and then put your credentials in an XML-file called ApiCredentials.xml. A sample can be found in the root directory of the application. The file with your credentials must then be put in the execution directory of the application.

### Camera
The software requires a Microsoft Kinect v2 camera and the Kinect SDK. 

You can download the SDK here: https://www.microsoft.com/en-us/download/details.aspx?id=44561
