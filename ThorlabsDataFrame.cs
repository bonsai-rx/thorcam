using OpenCV.Net;
using Thorlabs.TSI.ImageData;


namespace Bonsai.Thorlabs
{
    public class ThorlabsDataFrame
    {
        public ThorlabsDataFrame(IplImage image, Metadata metadata)
        {
            Image = image;
            Metadata = metadata;
        }

        public IplImage Image { get; private set; }

        public Metadata Metadata { get; private set; }
    }

    public class Metadata 
    {
        public uint FrameNumber { get; set; }
        public ulong Timestamp { get; set; }
    }
}
