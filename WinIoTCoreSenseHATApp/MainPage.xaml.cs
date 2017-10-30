using Emmellsoft.IoT.Rpi.SenseHat;
using Microsoft.Azure.Devices.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Gpio;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using WinIoTCoreSenseHATApp.Models;

// 空白ページの項目テンプレートについては、https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x411 を参照してください

namespace WinIoTCoreSenseHATApp
{
    /// <summary>
    /// それ自体で使用できる空白ページまたはフレーム内に移動できる空白ページ。
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public string connectionString = "<< Azure IoT Hub Connection String for Device Id >>";
        private int measureIntervalMSec = 10000;    // default 10 sec
        private int PhotoUploadIntervalSec = 30000; // default 30 sec
        public MainPage()
        {
            this.InitializeComponent();
            this.Loaded += MainPage_Loaded;
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            FixDeviceId();
            if (deviceId == "minwinpc")
            {
                Debug.Write("Please set deviceID or unique machine name");
                throw new ArgumentOutOfRangeException("Please set deviceID or unique machine name");
            }
            Debug.WriteLine("Fixed - DeviceId:" + deviceId);

            if (connectionString.StartsWith("<<") || !(connectionString.IndexOf("DeviceId=" + deviceId) > 0))
            {
                Debug.WriteLine("Please set collect 'connectionString'");
                throw new ArgumentOutOfRangeException("invalid connection string");
            }

            InitGpio();

            senseHat = await SenseHatFactory.GetSenseHat();
            LedArrayOff();
            senseHat.Display.Update();

            StartHoL();
        }

        private string deviceId = "";
        private ISenseHat senseHat;

        private async Task StartHoL()
        {
            StartSenseHatMeasuring();
            StartIoTHubSending();
            await StartPhotoUpload();
        }

        private void FixDeviceId()
        {
            foreach (var hn in Windows.Networking.Connectivity.NetworkInformation.GetHostNames())
            {
                IPAddress ipAddr;
                if (!hn.DisplayName.EndsWith(".local") && !IPAddress.TryParse(hn.DisplayName, out ipAddr))
                {
                    deviceId = hn.DisplayName;
                    break;
                }
            }
        }


        private void LedArrayOff()
        {
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    senseHat.Display.Screen[x, y] = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                }
            }
            senseHat.Display.Update();
        }

        private GpioPin motorPin;
        private int motorGpIoPin = 18; // Pin 12 6th from Right Top(0)
        private void InitGpio()
        {
            var gpio = GpioController.GetDefault();
            motorPin = gpio.OpenPin(motorGpIoPin);
            motorPin.SetDriveMode(GpioPinDriveMode.Output);
        }

        private DispatcherTimer measureTimer;
        private List<SenseHATSensorReading> measuredBuffer = new List<SenseHATSensorReading>();
        private void StartSenseHatMeasuring()
        {
            measureTimer = new DispatcherTimer();
            measureTimer.Interval = TimeSpan.FromMilliseconds(measureIntervalMSec);
            measureTimer.Tick += (s, o) =>
            {
                measureTimer.Stop();

                var sensor = senseHat.Sensors;
                sensor.HumiditySensor.Update();
                sensor.PressureSensor.Update();
                sensor.ImuSensor.Update();
                lock (this)
                {
                    var reading = new SenseHATSensorReading();
                    if (sensor.Temperature.HasValue)
                    {
                        reading.Temperature = sensor.Temperature.Value;
                    }
                    if (sensor.Humidity.HasValue)
                    {
                        reading.Humidity = sensor.Humidity.Value;
                    }
                    if (sensor.Pressure.HasValue)
                    {
                        reading.Pressure = sensor.Pressure.Value;
                    }
                    if (sensor.Acceleration.HasValue)
                    {
                        var accel = sensor.Acceleration.Value;
                        reading.AccelX = accel.X;
                        reading.AccelY = accel.Y;
                        reading.AccelZ = accel.Z;
                    }
                    if (sensor.Gyro.HasValue)
                    {
                        var gyro = sensor.Gyro.Value;
                        reading.GyroX = gyro.X;
                        reading.GyroY = gyro.Y;
                        reading.GyroZ = gyro.Z;
                    }
                    if (sensor.MagneticField.HasValue)
                    {
                        var mag = sensor.MagneticField.Value;
                        reading.MagX = mag.X;
                        reading.MagY = mag.Y;
                        reading.MagZ = mag.Z;
                    }
                    reading.MeasuredTime = DateTime.Now;
#if (ACCESS_IOT_HUB)
                    measuredBuffer.Add(reading);
#endif
                    Debug.WriteLine(String.Format("{0}:T={1},H={2},P={3},A={4}:{5}:{6},G={7}:{8}:{9},M={10}:{11}:{12}", reading.MeasuredTime.ToString("yyyyMMdd-HHmmss"), reading.Temperature, reading.Humidity, reading.Pressure, reading.AccelX, reading.AccelY, reading.AccelZ, reading.GyroX, reading.GyroY, reading.GyroZ, reading.MagX, reading.MagY, reading.MagZ));
                }
                measureTimer.Start();
            };
            measureTimer.Start();
        }

        DeviceClient deviceClient;

        DispatcherTimer uploadTimer;
        bool iotHubConnected = false;
        private void StartIoTHubSending()
        {
            try
            {
                deviceClient = DeviceClient.CreateFromConnectionString(connectionString, TransportType.Http1);
                iotHubConnected = true;
                uploadTimer = new DispatcherTimer();
                uploadTimer.Interval = TimeSpan.FromMilliseconds(measureIntervalMSec);
                uploadTimer.Tick +=async (o, e) => 
                {
                    await SendMessage();
                };
                uploadTimer.Start();
                ReceiveCommands();
            }
            catch(Exception ex)
            {
                Debug.WriteLine("IoT Connection Failed - " + ex.Message);
            }
        }
        int sendCount = 0;
        public async Task SendMessage()
        {
            uploadTimer.Stop();
            var sendReadings = new List<SensorReadingForIoT>();
            lock (this)
            {
                foreach (var sr in measuredBuffer)
                {
                    sendReadings.Add(new SensorReadingForIoT()
                    {
                        deviceId = deviceId,
                        AccelX = sr.AccelX,
                        AccelY = sr.AccelY,
                        AccelZ = sr.AccelZ,
                        GyroX = sr.GyroX,
                        GyroY = sr.GyroY,
                        GyroZ = sr.GyroZ,
                        Humidity = sr.Humidity,
                        MagX = sr.MagX,
                        MagY = sr.MagY,
                        MagZ = sr.MagZ,
                        msgId = deviceId + sr.MeasuredTime.ToString("yyyyMMddHHmmssfff"),
                        MeasuredTime = sr.MeasuredTime,
                        Pressure = sr.Pressure,
                        Temperature = sr.Temperature
                    });
                }
                measuredBuffer.Clear();
            }
            var content = Newtonsoft.Json.JsonConvert.SerializeObject(sendReadings);
            var message = new Message(Encoding.UTF8.GetBytes(content));
            try
            {
                if (!iotHubConnected)
                {
                    Debug.WriteLine("IoT Hub seems to not be connected!");
                }
                await deviceClient.SendEventAsync(message);
                Debug.WriteLine("Send[" + sendCount++ + "]" + measuredBuffer.Count + " messages @" + DateTime.Now);
            }
            catch(Exception ex)
            {
                Debug.WriteLine("Exception happned in sending - " + ex.Message);
                iotHubConnected = false;
            }
            uploadTimer.Start();
        }
        public async Task ReceiveCommands()
        {
            Debug.WriteLine("Device waiting for commands from IoT Hub...");
            Message receivedMessage;
            string messageData;
            while (true)
            {
                try
                {
                    receivedMessage = await deviceClient.ReceiveAsync();
                    if (receivedMessage != null)
                    {
                        messageData = Encoding.ASCII.GetString(receivedMessage.GetBytes());
                        Debug.WriteLine("\t{0}> Received message:{1}", DateTime.Now.ToLocalTime(), messageData);
                        if (messageData.ToLower().IndexOf("on") > 0)
                        {
                            motorPin.Write(Windows.Devices.Gpio.GpioPinValue.Low);
                        }
                        else
                        {
                            motorPin.Write(Windows.Devices.Gpio.GpioPinValue.High);
                        }
                        await deviceClient.CompleteAsync(receivedMessage);
                    }
                }
                catch(Exception ex)
                {
                    Debug.WriteLine("Exception Happened in receive waiting - " + ex.Message);
                    iotHubConnected = false;
                }
            }
        }
        MediaCapture mediaCaptureManager;
        string capturedPhotoFile = "captured.jpg";

        DispatcherTimer photoUploadTimer;

        private async Task StartPhotoUpload()
        {
#if (PHOTO_UPLOAD)
            mediaCaptureManager = new MediaCapture();
            try
            {
                await mediaCaptureManager.InitializeAsync();
                previewElement.Source = mediaCaptureManager;
                await mediaCaptureManager.StartPreviewAsync();
                photoUploadTimer = new DispatcherTimer();
                photoUploadTimer.Interval = TimeSpan.FromSeconds(PhotoUploadIntervalSec);
                photoUploadTimer.Tick +=async (s, o) =>
                {
                    await UploadPhoto();
                };
                photoUploadTimer.Start();
            }
            catch(Exception ex)
            {
                Debug.WriteLine("Exception Happen in initialize photo uploading - " + ex.Message);
            }
#endif

        }

        StorageFile photoStorageFile;

        private async Task UploadPhoto()
        {
            photoUploadTimer.Stop();
            photoStorageFile = await Windows.Storage.KnownFolders.PicturesLibrary.CreateFileAsync(capturedPhotoFile, CreationCollisionOption.ReplaceExisting);
            var imageProperties = ImageEncodingProperties.CreateJpeg();
            try
            {
                using (var fileStream = await photoStorageFile.OpenStreamForReadAsync())
                {
                    await mediaCaptureManager.CapturePhotoToStorageFileAsync(imageProperties, photoStorageFile);
                    var fileName = "img" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + ".jpg";
                    await deviceClient.UploadToBlobAsync(fileName, fileStream);
                    Debug.WriteLine(string.Format("Uploaded: {0} at {1}", fileName, DateTime.Now.ToString("yyyy/MM/dd - hh:mm:ss")));
                }
            }
            catch (Exception ex)
            {
                Debug.Write(ex.Message);
            }
            photoUploadTimer.Start();
        }

    }


}
