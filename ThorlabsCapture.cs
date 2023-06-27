using OpenCV.Net;
using System;
using System.ComponentModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Thorlabs.TSI.ColorInterfaces;
using Thorlabs.TSI.ColorProcessor;
using Thorlabs.TSI.Core;
using Thorlabs.TSI.CoreInterfaces;
using Thorlabs.TSI.Demosaicker;
using Thorlabs.TSI.ImageData;
using Thorlabs.TSI.ImageDataInterfaces;
using Thorlabs.TSI.TLCamera;
using Thorlabs.TSI.TLCameraInterfaces;


namespace Bonsai.Thorlabs
{
    [Combinator(MethodName = nameof(Generate))]
    [WorkflowElementCategory(ElementCategory.Source)]

    [Description("Acquires a sequence of images from a Thorlabs camera.")]
    public class ThorlabsCapture : Source<ThorlabsDataFrame>
    {
        static readonly object systemLock = new object();

        [TypeConverter(typeof(SerialNumberConverter))]
        [Description("The optional serial number of the camera from which to acquire images.")]
        public string SerialNumber { get; set; }

        [Description("Frame exposure time (in microseconds).")]
        public int Exposure { get; set; } = 5000;

        private CameraSensorType cameraSensorType { get; set; } = CameraSensorType.Bayer;

        private ushort[] _demosaickedData = null;
        private ushort[] _processedImage = null;
        private Demosaicker _demosaicker = new Demosaicker();

        private ColorFilterArrayPhase _colorFilterArrayPhase;
        private ColorProcessor _colorProcessor = null;
        private bool _isColor = false;
        private ColorProcessorSDK _colorProcessorSDK = null;


        protected virtual void Configure(ITLCamera camera)
        {
            camera.Disarm();
            camera.ExposureTime_us = 50000;
            camera.MaximumNumberOfFramesToQueue = 2;
            _isColor = camera.CameraSensorType == cameraSensorType;
            if (_isColor)
            {
                _colorProcessorSDK = new ColorProcessorSDK();
                _colorFilterArrayPhase = camera.ColorFilterArrayPhase;
                var colorCorrectionMatrix = camera.GetCameraColorCorrectionMatrix();
                var whiteBalanceMatrix = camera.GetDefaultWhiteBalanceMatrix();
                _colorProcessor = (ColorProcessor)_colorProcessorSDK.CreateStandardRGBColorProcessor(whiteBalanceMatrix, colorCorrectionMatrix, (int)camera.BitDepth);
            }
            camera.OperationMode = OperationMode.HardwareTriggered;
            camera.Arm();
        }


        public override IObservable<ThorlabsDataFrame> Generate()
        {
            return Generate(Observable.Return(Unit.Default));
        }

        public IObservable<ThorlabsDataFrame> Generate<TSource>(IObservable<TSource> start)
        {
            return Observable.Create<ThorlabsDataFrame>((observer, cancellationToken) =>
            {
                return Task.Factory.StartNew(async () =>
                {
                    ITLCameraSDK _tlCameraSDK = TLCameraSDK.OpenTLCameraSDK();
                    ITLCamera camera = _tlCameraSDK.OpenCamera(SerialNumber, true);
                    try
                    {
                        Configure(camera);
                        await start;

                        using (var cancellation = cancellationToken.Register(camera.Dispose))
                        {
                            while (!cancellationToken.IsCancellationRequested)
                            {
                                if (camera.NumberOfQueuedFrames > 0)
                                {
                                    var frame = camera.GetPendingFrameOrNull();
                                    ImageDataUShort1D imageData;
                                    ushort[] rawData;
                                    IplImage img;

                                    if (frame != null)
                                    {
                                        if (_isColor)
                                        {
                                            rawData = ((IImageDataUShort1D)frame.ImageData).ImageData_monoOrBGR;
                                            var size = frame.ImageData.Width_pixels * frame.ImageData.Height_pixels * 3;
                                            if ((_demosaickedData == null) || (size != _demosaickedData.Length))
                                            {
                                                _demosaickedData = new ushort[frame.ImageData.Width_pixels * frame.ImageData.Height_pixels * 3];
                                            }
                                            _demosaicker.Demosaic(
                                                frame.ImageData.Width_pixels,
                                                frame.ImageData.Height_pixels,
                                                0,
                                                0,
                                                _colorFilterArrayPhase,
                                                ColorFormat.BGRPixel,
                                                ColorSensorType.Bayer,
                                                frame.ImageData.BitDepth,
                                                rawData,
                                                _demosaickedData);

                                            if ((_processedImage == null) || (size != _demosaickedData.Length))
                                            {
                                                _processedImage = new ushort[frame.ImageData.Width_pixels * frame.ImageData.Height_pixels * 3];
                                            }
                                            ushort maxValue = (ushort)((1 << frame.ImageData.BitDepth) - 1);
                                            _colorProcessor.Transform48To48(
                                                _demosaickedData,
                                                ColorFormat.BGRPixel,
                                                0, maxValue,
                                                0, maxValue,
                                                0, maxValue,
                                                0, 0, 0,
                                                _processedImage,
                                                ColorFormat.BGRPixel);
                                            imageData = new ImageDataUShort1D(
                                                _processedImage,
                                                frame.ImageData.Width_pixels,
                                                frame.ImageData.Height_pixels,
                                                frame.ImageData.BitDepth,
                                                ImageDataFormat.BGRPixel);
                                            int width = (imageData.Width_pixels);
                                            int height = (imageData.Height_pixels);
                                            img = new IplImage(new Size(width, height), IplDepth.U16, imageData.NumberOfChannels);
                                            using (var buffer = Mat.CreateMatHeader(_processedImage, height, width, Depth.U16, imageData.NumberOfChannels))
                                            {
                                                CV.Copy(buffer, img);
                                            }
                                        }
                                        else
                                        {
                                            rawData = ((IImageDataUShort1D)frame.ImageData).ImageData_monoOrBGR;
                                            imageData = ((ImageDataUShort1D)(frame.ImageData));
                                            int width = (imageData.Width_pixels);
                                            int height = (imageData.Height_pixels);
                                            img = new IplImage(new Size(width, height), IplDepth.U16, imageData.NumberOfChannels);
                                            using (var buffer = Mat.CreateMatHeader(rawData, height, width, Depth.U16, imageData.NumberOfChannels))
                                            {
                                                CV.Copy(buffer, img);
                                            }
                                            observer.OnNext(new ThorlabsDataFrame(
                                                img,
                                                new Metadata{
                                                    FrameNumber = frame.FrameNumber,
                                                    Timestamp = frame.TimeStampRelative_ns_OrNull.HasValue ? frame.TimeStampRelative_ns_OrNull.Value : 0}
                                                ));
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex) {
                        Console.WriteLine(ex);
                        observer.OnError(ex); throw; }
                    finally
                    {
                        camera.Disarm();
                        camera.Dispose();
                    }
                },
                cancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            });
        }
    }
}
