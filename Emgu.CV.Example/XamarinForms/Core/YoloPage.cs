﻿//----------------------------------------------------------------------------
//  Copyright (C) 2004-2020 by EMGU Corporation. All rights reserved.       
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.IO;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
#if __ANDROID__
using Android.App;
using Android.Content;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;
using Android.Graphics;
using Android.Preferences;
#endif

using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Dnn;
using Emgu.CV.Face;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Emgu.Models;
using Emgu.Util;

using Color = Xamarin.Forms.Color;
using Environment = System.Environment;
using Point = System.Drawing.Point;

namespace Emgu.CV.XamarinForms
{
    public class YoloPage
#if __ANDROID__
        : AndroidCameraPage
#else
        : ButtonTextImagePage
#endif
    {
        private String _modelFolderName = "yolo_v3";
        private Net _yoloDetector = null;
        private string[] _labels = null;

        public enum YoloVersion
        {
            YoloV3,
            YoloV3Spp,
            YoloV3Tiny
        }

        private async Task InitYoloDetector(YoloVersion version = YoloVersion.YoloV3)
        {
            if (_yoloDetector == null)
            {
                FileDownloadManager manager = new FileDownloadManager();

                if (version == YoloVersion.YoloV3Spp)
                {
                    manager.AddFile(
                        "https://pjreddie.com/media/files/yolov3-spp.weights",
                        _modelFolderName);
                    manager.AddFile(
                        "https://github.com/pjreddie/darknet/raw/master/cfg/yolov3-spp.cfg",
                        _modelFolderName);
                } else if (version == YoloVersion.YoloV3)
                {
                    manager.AddFile(
                        "https://pjreddie.com/media/files/yolov3.weights",
                        _modelFolderName);
                    manager.AddFile(
                        "https://github.com/pjreddie/darknet/raw/master/cfg/yolov3.cfg",
                        _modelFolderName);
                } else if (version == YoloVersion.YoloV3Tiny)
                {
                    manager.AddFile(
                        "https://pjreddie.com/media/files/yolov3-tiny.weights",
                        _modelFolderName);
                    manager.AddFile(
                        "https://github.com/pjreddie/darknet/raw/master/cfg/yolov3-tiny.cfg",
                        _modelFolderName);
                }

                manager.AddFile("https://github.com/pjreddie/darknet/raw/master/data/coco.names",
                    _modelFolderName);
            
                manager.OnDownloadProgressChanged += DownloadManager_OnDownloadProgressChanged;
                await manager.Download();
                _yoloDetector = DnnInvoke.ReadNetFromDarknet(manager.Files[1].LocalFile, manager.Files[0].LocalFile);
                _labels = File.ReadAllLines(manager.Files[2].LocalFile);
            }
        }

        private Mat _inputBlob = new Mat();

        private void DetectAndRender(Mat image, double confThreshold = 0.5)
        {
            //int imgDim = 300;
            MCvScalar meanVal = new MCvScalar();

            Size imageSize = image.Size;
            //using (Mat inputBlob = new Mat())
            //{
                DnnInvoke.BlobFromImage(
                    image,
                    _inputBlob,
                    1.0,
                    new Size(416, 416),
                    meanVal,
                    true,
                    false,
                    DepthType.Cv8U);
                _yoloDetector.SetInput(_inputBlob, "", 0.00392);
                int[] outLayers = _yoloDetector.UnconnectedOutLayers;
                String outLayerType = _yoloDetector.GetLayer(outLayers[0]).Type;
                String[] outLayerNames = _yoloDetector.UnconnectedOutLayersNames;
                using (VectorOfMat outs = new VectorOfMat())
                {
                    _yoloDetector.Forward(outs, outLayerNames);
                    List<Rectangle> boxes = new List<Rectangle>();
                    List<double> confidents = new List<double>();
                    List<int> classIds = new List<int>();
                    if (outLayerType.Equals("Region"))
                    {
                        int size = outs.Size;
                        
                        for (int i = 0; i < size; i++)
                        {
                            // Network produces output blob with a shape NxC where N is a number of
                            // detected objects and C is a number of classes + 4 where the first 4
                            // numbers are [center_x, center_y, width, height]
                            using (Mat m = outs[i])
                            {
                                int rows = m.Rows;
                                int cols = m.Cols;
                                float[,] data = m.GetData(true) as float[,];
                                for (int j = 0; j < rows; j++)
                                {
                                    using (Mat subM = new Mat(m, new Emgu.CV.Structure.Range(j, j + 1), new Emgu.CV.Structure.Range(5, cols)))
                                    {
                                        double minVal = 0, maxVal = 0;
                                        Point minLoc = new Point();
                                        Point maxLoc = new Point();
                                        CvInvoke.MinMaxLoc(subM, ref minVal, ref maxVal, ref minLoc, ref maxLoc );
                                        if (maxVal > confThreshold)
                                        {
                                            
                                            int centerX = (int) (data[j,0] * imageSize.Width);
                                            int centerY = (int) (data[j,1] * imageSize.Height);
                                            int width = (int) (data[j,2] * imageSize.Width);
                                            int height = (int) (data[j,3] * imageSize.Height);
                                            int left = centerX - width / 2;
                                            int top = centerY - height / 2;
                                            Rectangle rect = new Rectangle(left, top, width, height);

                                            classIds.Add(maxLoc.X);
                                            confidents.Add(maxVal);
                                            boxes.Add(rect);
                                        }
                                    }
                                }

                            }
                        }

                        for  ( int i = 0; i < boxes.Count; i++)
                        {
                            String c = _labels[classIds[i]];
                            
                            CvInvoke.Rectangle(image, boxes[i], new MCvScalar(0, 0, 255), 2);
                            CvInvoke.PutText(
                                image, 
                                String.Format("{0}: {1}",c, confidents[i]),
                                boxes[i].Location,
                                FontFace.HersheyDuplex,
                                1.0, 
                                new MCvScalar(0, 0, 255),
                                1);
                        }
                        
                    }
                    else
                    {
                        throw new Exception(String.Format("Unknown output layer type: {0}", outLayerType ));
                    }
                //}
            }
        }

        private VideoCapture _capture = null;
        private Mat _mat = null;
        private String _defaultButtonText = "Yolo Detection";

#if __ANDROID__
        private String _StopCameraButtonText = "Stop Camera";
        private bool _isBusy = false;
#endif
        public YoloPage()
            : base()
        {
#if __ANDROID__
            HasCameraOption = true;
#endif

            var button = this.GetButton();
            button.Text = _defaultButtonText;
            button.Clicked += OnButtonClicked;

            OnImagesLoaded += async (sender, image) =>
            {
                if (image == null || (image.Length > 0 && image[0] == null))
                    return;

                if (image.Length == 0)
                {
                    await InitYoloDetector(YoloVersion.YoloV3Tiny);

#if __ANDROID__
                    button.Text = _StopCameraButtonText;
                    StartCapture(async delegate(Object sender, Mat m)
                    {
                        //Skip the frame if busy, 
                        //Otherwise too many frames arriving and will eventually saturated the memory.
                        if (!_isBusy)
                        {
                            _isBusy = true;
                            try
                            {
                                Stopwatch watch = Stopwatch.StartNew();
                                await Task.Run(() => { DetectAndRender(m); });
                                watch.Stop();
                                SetImage(m);
                                SetMessage(String.Format("Detected in {0} milliseconds.", watch.ElapsedMilliseconds));
                            }
                            finally
                            {
                                _isBusy = false;
                            }
                        }
                    });
#else
                    //Handle video
                    if (_capture == null)
                    {
                        _capture = new VideoCapture();
                        _capture.ImageGrabbed += _capture_ImageGrabbed;
                    }
                    _capture.Start();
#endif
                }
                else
                {
                    SetMessage("Please wait...");
                    SetImage(null);

                    await InitYoloDetector();

                    Stopwatch watch = Stopwatch.StartNew();

                    DetectAndRender(image[0]);
                    watch.Stop();

                    SetImage(image[0]);
                    String computeDevice = CvInvoke.UseOpenCL ? "OpenCL: " + Ocl.Device.Default.Name : "CPU";

                    SetMessage(String.Format("Detected in {0} milliseconds.", watch.ElapsedMilliseconds));
                }
            };
        }

        private void _capture_ImageGrabbed(object sender, EventArgs e)
        {
            if (_mat == null)
                _mat = new Mat();
            _capture.Retrieve(_mat);
            Stopwatch watch = Stopwatch.StartNew();
            DetectAndRender(_mat);
            watch.Stop();
            SetImage(_mat);
            this.DisplayImage.BackgroundColor = Color.Black;
            this.DisplayImage.IsEnabled = true;
            SetMessage(String.Format("Detected in {0} milliseconds.", watch.ElapsedMilliseconds));
        }

        private void OnButtonClicked(Object sender, EventArgs args)
        {
#if __ANDROID__
            var button = GetButton();
            if (button.Text.Equals(_StopCameraButtonText))
            {
                StopCapture();
                button.Text = _defaultButtonText;
                //AndroidImageView.Visibility = ViewStates.Invisible;
                return;
            }
#endif
            LoadImages(new string[] { "dog416.png" });
        }

        private void DownloadManager_OnDownloadProgressChanged(object sender, System.Net.DownloadProgressChangedEventArgs e)
        {
            if (e.TotalBytesToReceive <= 0)
                SetMessage(String.Format("{0} bytes downloaded.", e.BytesReceived));
            else
                SetMessage(String.Format("{0} of {1} bytes downloaded ({2}%)", e.BytesReceived, e.TotalBytesToReceive, e.ProgressPercentage));
        }
    }
}
