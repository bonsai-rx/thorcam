using Thorlabs.TSI.TLCamera;
using Thorlabs.TSI.TLCameraInterfaces;
using System.Collections.Generic;
using System.ComponentModel;

namespace Bonsai.Thorlabs
{
    class SerialNumberConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
        {
            return true;
        }

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {

            var values = new List<string>();
            using (var cameraSdk = TLCameraSDK.OpenTLCameraSDK())
            {
                var cameraList = cameraSdk.DiscoverAvailableCameras();
                for (int i = 0; i < cameraList.Count; i++)
                {
                    var currentSerial = cameraList[i];
                    if (currentSerial != null)
                    {
                        values.Add(currentSerial);
                    }
                }
            }
            return new StandardValuesCollection(values);
        }
    }
}
