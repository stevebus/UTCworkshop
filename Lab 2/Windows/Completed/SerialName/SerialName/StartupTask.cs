using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Http;
using Windows.ApplicationModel.Background;
using Windows.Devices.SerialCommunication;
using Windows.Devices.Enumeration;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

// The Background Application template is documented at http://go.microsoft.com/fwlink/?LinkID=533884&clcid=0x409

namespace SerialName
{
    public sealed class StartupTask : IBackgroundTask
    {

        private ObservableCollection<DeviceInformation> listOfDevices = new ObservableCollection<DeviceInformation>();
        private BackgroundTaskDeferral deferral;


        public void Run(IBackgroundTaskInstance taskInstance)
        {
            deferral = taskInstance.GetDeferral();
            ListAvailablePorts().Wait();
        }

        private async Task ListAvailablePorts()
        {
            string aqs = SerialDevice.GetDeviceSelector();
            System.Diagnostics.Debug.WriteLine(aqs);
            var dis = await DeviceInformation.FindAllAsync(aqs);

            for (int i = 0; i < dis.Count; i++)
            {
                listOfDevices.Add(dis[i]);
                string t = "Device name:  " + dis[i].Id;
                System.Diagnostics.Debug.WriteLine(t);
                t = "Device description:  " + dis[i].Name;
                System.Diagnostics.Debug.WriteLine(t);
            }
        }
    }
}
