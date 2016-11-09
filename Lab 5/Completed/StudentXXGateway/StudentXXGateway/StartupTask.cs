using System;
using System.Text;
using Windows.ApplicationModel.Background;
using Newtonsoft.Json;
using Microsoft.Azure.Devices.Client;
using Windows.Devices.SerialCommunication;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using System.Diagnostics;

// The Background Application template is documented at http://go.microsoft.com/fwlink/?LinkID=533884&clcid=0x409

namespace StudentXXGateway
{
    public sealed class StartupTask : IBackgroundTask
    {
         
        private BackgroundTaskDeferral deferral;

        static DeviceClient deviceClient;
        private static string iotHubUri = "<iothubnamefromchuck>.azure-devices.net";  // <iothubname>.azure-devices.net
        private string deviceId = "studentXX"; // replace XX with your studentID
        private static string deviceKey = "<device ID from chuck>";  // use your key, not mine!  :-)

        // classes related to serial communication
        static string serialDeviceName = @"<your serial port name>";
        // example serial port name \\?\FTDIBUS#VID_0403+PID_6001+AL01GQQHA#0000#{86e0d1e0-8089-11d0-9ce4-08003e301f73}
        private static SerialDevice serialPort;
        private static DataReader dataReaderObject = null;
        private static DataWriter dataWriteObject = null;


        // graysville, al  33.62059, -86.968246
        static double latitude = 33.62059;
        static double longitude = -86.968246;

        public void Run(IBackgroundTaskInstance taskInstance)
        {
            // get a deferral token.  this keeps the app from 'exiting' until we want it to
            deferral = taskInstance.GetDeferral();

            // create the connection to IoTHub, based on the URI, device ID, and key from above
            deviceClient = DeviceClient.Create(iotHubUri,
                AuthenticationMethodFactory.
                    CreateAuthenticationWithRegistrySymmetricKey(deviceId, deviceKey),
                TransportType.Http1);

            SendDeviceInfo();

            // connect to the serial port
            SetupSerialConnection().Wait();

            Debug.WriteLine("Setting up Data Reader");
            // get a pointer to the buffer of data that has been sent to the serial port so we can read it
            dataReaderObject = new DataReader(serialPort.InputStream);

            Debug.WriteLine("Wiring up Command Receiver...");
            // start the thread to listen for "commands" from IoThub (i.e. turn our LED on/off)
            ReceiveCommands();   //.Start();

            // loop forever and receive serial data and sent to IoTHub
            Debug.WriteLine("Starting receive loop");
            while (true)
            {
                try {
                    ReadAsync().Wait();
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.Message);
                }
            }

        }
        private void SendDeviceInfo()
        {

            string createdDateTime = DateTime.UtcNow.ToString("o");

            // create a JSON string that represents the device metadata
            string deviceInfo = "{\"DeviceProperties\":{\"DeviceID\":\"" +
                deviceId + "\",\"HubEnabledState\":true,\"CreatedTime\":\"" +
                createdDateTime + "\",\"DeviceState\":\"normal\",\"UpdatedTime\":null,\"Manufacturer\":\"Busbyland Electronics\",\"ModelNumber\":\"RPI35073\",\"SerialNumber\":\"35073\",\"FirmwareVersion\":\"1.0\",\"Platform\":\"Raspberry Pi 2\",\"Processor\":\"Intel Atom\",\"InstalledRAM\":\"4GB\",\"Latitude\":" +
                latitude.ToString() + ",\"Longitude\":" + longitude.ToString() +
                "},\"Commands\":[{\"Name\":\"ON\",\"Parameters\":null},{\"Name\":\"OFF\",\"Parameters\":null}],\"CommandHistory\":[],\"IsSimulatedDevice\":false,\"Version\":\"1.0\",\"ObjectType\":\"DeviceInfo\"}";

            // send the metadata to IoTHub
            SendDeviceToCloudMessagesAsync(deviceInfo);

        }
        private async Task SetupSerialConnection()
        {
            // get a handle to the serial device specified earlier
            serialPort = await SerialDevice.FromIdAsync(serialDeviceName);

            if (serialPort == null)
                Debug.WriteLine("Oops - cannot connect to serial port");

            Debug.WriteLine("connected to serial port");

            // Configure serial settings
            serialPort.WriteTimeout = TimeSpan.FromMilliseconds(1000);
            serialPort.ReadTimeout = TimeSpan.FromMilliseconds(1000);
            serialPort.BaudRate = 9600;
            serialPort.Parity = SerialParity.None;
            serialPort.StopBits = SerialStopBitCount.One;
            serialPort.DataBits = 8;
            serialPort.Handshake = SerialHandshake.None;

            // Display configured settings
            string status;
            status = "Serial port configured successfully: ";
            status += serialPort.BaudRate + "-";
            status += serialPort.DataBits + "-";
            status += serialPort.Parity.ToString() + "-";
            status += serialPort.StopBits;
            Debug.WriteLine(status);

        }

        private async Task ReadAsync()
        {

            // for the lab, we have a fixed message length, so we can just read that many bytes at a time
            // otherwise we have to read a byte at a time and look for newlines
            uint ReadBufferLength = 13;

            // Set InputStreamOptions to complete the asynchronous read operation when one or more bytes is available
            dataReaderObject.InputStreamOptions = InputStreamOptions.Partial;

            // load the reader with data from the buffer
            UInt32 bytesRead = await dataReaderObject.LoadAsync(ReadBufferLength);

            // did we get a message?
            if (bytesRead > 0)
            {
                // get data out of the buffer as a string
                string x = dataReaderObject.ReadString(bytesRead);

                // chop off the CRLF
                x = x.Substring(0, x.IndexOf("\r\n"));

                // split the humidity and temp into an array
                string[] readings = x.Split(',');

                string tempStr = string.Format("Humidity={0}, Temperature={1}", readings[0], readings[1]);
                System.Diagnostics.Debug.WriteLine(tempStr);

                Random r = new Random();

                // create a new telemetryDataPoint object to hold the data
                var telemetryDataPoint = new
                {
                    DeviceId = deviceId,
                    Temperature = readings[1],
                    Humidity = readings[0],
                    RPM = (r.NextDouble() * 10.0) + 25,
                    ExternalTemperature = 0
                };

                // serialize the telemetryDataPoint object into JSON
                string messageString = JsonConvert.SerializeObject(telemetryDataPoint);

                // send the message to IotHub
                SendDeviceToCloudMessagesAsync(messageString);
            }
        }
        static async void SendDeviceToCloudMessagesAsync(string messageToSend)
        {
            // conver message to a byte array and wrap it with IoTHub message metadata
            var message = new Message(Encoding.ASCII.GetBytes(messageToSend));

            // send the message to IoTHub
            await deviceClient.SendEventAsync(message);

        }
        static async Task ReceiveCommands()
        {
            Message receivedMessage;
            string messageData;

            while (true)
            {
                // checck for a messages
                receivedMessage = await deviceClient.ReceiveAsync();

                // did we receive a message?
                if (receivedMessage != null)
                {
                    // unwrap the messages object and get the message string
                    messageData = Encoding.ASCII.GetString(receivedMessage.GetBytes());
                    string tempStr = String.Format("******Received Command: {0}", messageData);
                    Debug.WriteLine(tempStr);

                    // tell IoTHub that we got the message (updates the status in the portal)
                    await deviceClient.CompleteAsync(receivedMessage);

                    // see what the command was and act on it (send to arduino)
                    if (messageData.Contains("ON"))
                    {
                        Debug.WriteLine("Turning LED on");
                        WriteSerialAsync("ON").Wait();
                    }
                    else if (messageData.Contains("OFF"))
                    {
                        Debug.WriteLine("Turning LED Off");
                        WriteSerialAsync("OFF").Wait();
                    }
                    else
                    {
                        Debug.WriteLine("Unrecognized command - ignoring...");
                    }


                }

                // wait before polling again  (only have to do this if it's HTTP connection)
                System.Threading.Tasks.Task.Delay(10000).Wait();
            }
        }

        static async Task WriteSerialAsync(string Message)
        {

            // get a handle to the output buffer of the serial port so we can write to it
            dataWriteObject = new DataWriter(serialPort.OutputStream);

            // write the data and append a newline (arduino uses this to parse)
            // this line only writes to internal buffer
            dataWriteObject.WriteString(Message + '\n');

            // write to serial port
            UInt32 bytesWritten = await dataWriteObject.StoreAsync();

            if (bytesWritten > 0)
            {
                string tempStr = Message + " ";
                tempStr += " written successfully!";
            }

            // detach our handle to the serial buffer and destroy

            dataWriteObject.DetachStream();
            dataWriteObject.Dispose();

        }


    }
}
