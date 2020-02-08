using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
    using System.IO;
using WinForms = System.Windows.Forms;
using System.Threading;
using System.Diagnostics;
using Newtonsoft.Json;

namespace Video_Survey_Database_Extractor
{
    /// <summary>
    /// Interação lógica para MainWindow.xam
    /// </summary>
    public partial class MainWindow : Window
    {
        private PXCMSenseManager sm;
        private Thread processingThread;
        private Paths paths;
        private string input_folder = null;
        private string output_folder = null;        
        Dictionary<string, Paths> dictPaths = new Dictionary<string, Paths>();        
        List<string> dirsSource;
        List<string> dirsOutput;   

        public MainWindow()
        {
            InitializeComponent();
            sourceButton.IsEnabled = true;
            outputButton.IsEnabled = false;
            stopButton.IsEnabled = false;
            ExtractButton.IsEnabled = false;
            setupButton.IsEnabled = false;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            processingThread.Abort();
            sm.Dispose();
        }

        private void ProcessingThread()
        {
            string videoName, nameColor, nameDepth, nameIr;
            int width = 640;
            int height = 480;

            int frameIndex = 0;
            string formatImageFile = ".png";
            int nframes = 0;
            int lostFrames = 0;
            string landmarks = null;
            long frameTimeStamp = 0;            
            PXCMImage color;
            PXCMImage depth;
            PXCMImage ir;
            PXCMCapture.Sample sample;
            PXCMImage.ImageData imageColor;
            PXCMImage.ImageData imageDepth;
            PXCMImage.ImageData imageIr;
            WriteableBitmap wbm1, wbm2, wbm3;
            Int32Rect rect2crop;
            PXCMFaceModule faceModule;
            PXCMFaceConfiguration faceConfig;
            PXCMFaceData faceData = null;
            //Offset Cropped rectangle
            Offset offset = new Offset(0, 0, 0, 0);

            //For each directory, extract all landmarks or images streams from all videos
            foreach (var dir in dirsSource)
            {
                //If the folder is not empty
                if (Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    dictPaths.TryGetValue(dir, out paths); //This dict contains all source and output dirs
                    List<string> fileList = new List<string>(Directory.GetFiles(dir, "*.rssdk"));
                    //For each video
                    foreach (var inputFile in fileList)
                    {
                        lostFrames = 0;
                        videoName = inputFile.Split('\\').Last().Split('.')[0];                        
                        // Create a SenseManager instance
                        sm = PXCMSenseManager.CreateInstance();
                        // Recording mode: true
                        // Playback mode: false
                        // Settings for playback mode (read rssdk files and extract frames)
                        sm.captureManager.SetFileName(inputFile, false);
                        sm.captureManager.SetRealtime(false);
                        nframes = sm.captureManager.QueryNumberOfFrames();

                        //Update in realtime the current extraction
                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            textBox2.Text = nframes.ToString();
                            textBox3.Text = String.Format("Record: {0}\nVideo: {1}", paths.root, videoName);
                        }));

                        sm.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_COLOR, width, height, 0);
                        sm.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_DEPTH, width, height);
                        sm.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_IR, width, height);

                        //Extract Landmarks
                        sm.EnableFace();
                        faceModule = sm.QueryFace();
                        faceConfig = faceModule.CreateActiveConfiguration();
                        faceConfig.landmarks.maxTrackedFaces = 1;
                        faceConfig.landmarks.isEnabled = true;
                        faceConfig.detection.maxTrackedFaces = 1;
                        faceConfig.detection.isEnabled = true;
                        faceConfig.EnableAllAlerts();                       
                        faceConfig.ApplyChanges();

                        sm.Init();

                        // This string stores all data before saving to csv file
                        landmarks = null;
                        // Start AcquireFrame/ReleaseFrame loop
                        var stopwatch = new Stopwatch();
                        stopwatch.Start();

                        while (sm.AcquireFrame(true) >= pxcmStatus.PXCM_STATUS_NO_ERROR)
                        {
                            // Retrieve face data
                            faceModule = sm.QueryFace();
                            frameIndex = sm.captureManager.QueryFrameIndex();
                            if (faceModule != null)
                            {
                                // Retrieve the most recent processed data
                                faceData = faceModule.CreateOutput();
                                faceData.Update();
                            }
                            if (faceData != null)
                            {
                                Int32 nfaces = faceData.QueryNumberOfDetectedFaces();

                                frameTimeStamp = sm.captureManager.QueryFrameTimeStamp();
                                //PXCMCapture.Sample sample = senseManager.QuerySample();
                                if (nfaces == 0) //If none face was detected, we will consider as a "lost frame"
                                {
                                    lostFrames += 1;
                                }
                                for (Int32 i = 0; i < nfaces; i++)
                                {
                                    //Retrieve the image                                    
                                    sample = sm.QuerySample();
                                    // Work on the images
                                    color = sample.color;
                                    depth = sample.depth;
                                    ir = sample.ir;

                                    PXCMFaceData.Face face = faceData.QueryFaceByIndex(i);                                    
                                    PXCMFaceData.LandmarksData landmarkData = face.QueryLandmarks();
                                    PXCMFaceData.DetectionData ddata = face.QueryDetection();
                                    PXCMFaceData.PoseData poseData = face.QueryPose();
                                    poseData.QueryHeadPosition(out PXCMFaceData.HeadPosition headPosition);
                                    poseData.QueryPoseAngles(out PXCMFaceData.PoseEulerAngles poseEulerAngles);
                                    Debug.WriteLine(headPosition.headCenter.x + " " + headPosition.headCenter.y + " " + headPosition.headCenter.z + " " + poseEulerAngles.pitch + " " + poseEulerAngles.roll + " " + poseEulerAngles.yaw);

                                    //Rectangle coordenates from detected face
                                    ddata.QueryBoundingRect(out PXCMRectI32 rect);

                                    //See the offset struct to define the values
                                    rect2crop = new Int32Rect(rect.x + offset.x, rect.y + offset.y, rect.w + offset.w, rect.h + offset.h);
                                    ddata.QueryFaceAverageDepth(out Single depthDistance);

                                    color.AcquireAccess(PXCMImage.Access.ACCESS_READ, PXCMImage.PixelFormat.PIXEL_FORMAT_RGB32, out imageColor);
                                    depth.AcquireAccess(PXCMImage.Access.ACCESS_READ, PXCMImage.PixelFormat.PIXEL_FORMAT_DEPTH_RAW, out imageDepth);
                                    ir.AcquireAccess(PXCMImage.Access.ACCESS_READ, PXCMImage.PixelFormat.PIXEL_FORMAT_RGB24, out imageIr);

                                    //Convert it to Bitmap
                                    wbm1 = imageColor.ToWritableBitmap(0, color.info.width, color.info.height, 100.0, 100.0);
                                    wbm2 = imageDepth.ToWritableBitmap(0, depth.info.width, depth.info.height, 100.0, 100.0);
                                    wbm3 = imageIr.ToWritableBitmap(0, ir.info.width, ir.info.height, 100.0, 100.0);

                                    color.ReleaseAccess(imageColor);
                                    depth.ReleaseAccess(imageDepth);
                                    ir.ReleaseAccess(imageIr);                                   

                                    nameColor = paths.rgbFolder + "\\" + videoName + "\\" + videoName + "_color_" + frameIndex + formatImageFile;
                                    nameDepth = paths.depthFolder + "\\" + videoName + "\\" + videoName + "_depth_" + frameIndex + formatImageFile;
                                    nameIr = paths.irFolder + "\\" + videoName + "\\" + videoName + "_ir_" + frameIndex + formatImageFile;
                              
                                    //Crops the face images!
                                    CreateThumbnail(nameColor, new CroppedBitmap(wbm1, rect2crop));
                                    CreateThumbnail(nameDepth, new CroppedBitmap(wbm2, rect2crop));
                                    CreateThumbnail(nameIr, new CroppedBitmap(wbm3, rect2crop));

                                    //Debug.WriteLine((depthDistance /1000 ) + " m" + " " + rect.x + " " + rect.y + " " + rect.w + " " + rect.h);
                                    /*
                                    x - The horizontal coordinate of the top left pixel of the rectangle.
                                    y - The vertical coordinate of the top left pixel of the rectangle.
                                    w - The rectangle width in pixels.
                                    h -The rectangle height in pixels.*/

                                    if (landmarkData != null)
                                    {
                                        PXCMFaceData.LandmarkPoint[] landmarkPoints;
                                        landmarkData.QueryPoints(out landmarkPoints);

                                        Application.Current.Dispatcher.BeginInvoke(new Action(() => textBox1.Text = frameIndex.ToString()));
                                        
                                        landmarks += inputFile.Split('\\').Last() + ";" + frameIndex + ";" + nameColor + ";" + nameDepth + ";" + nameIr + ";" + frameTimeStamp + ";" + depthDistance.ToString("F") + ";" + poseEulerAngles.yaw.ToString("F") + ";" + poseEulerAngles.pitch.ToString("F") + ";" + poseEulerAngles.roll.ToString("F") + ";"; // Begin line with frame info

                                        for (int j = 0; j < landmarkPoints.Length; j++) // Writes landmarks coordinates along the line 
                                        {
                                            //get world coordinates
                                            landmarks += /*landmarkPoints[j].source.index + ";" +*/ (landmarkPoints[j].world.x * 1000).ToString("F") + ";" + (landmarkPoints[j].world.y * 1000).ToString("F") + ";" + (landmarkPoints[j].world.z * 1000).ToString("F") + ";";
                                        }
                                        for (int j = 0; j < landmarkPoints.Length; j++)
                                        {
                                            //get coordinate of the image pixel
                                            landmarks += /*landmarkPoints[j].confidenceImage + ";" + */landmarkPoints[j].image.x.ToString("F") + ";" + landmarkPoints[j].image.y.ToString("F") + ";";
                                        }
                                        landmarks += '\n'; // Breaks line after the end of the frame coordinates                                        
                                    }
                                }
                            }
                            // Release the frame
                            if (faceData != null)
                                faceData.Dispose();
                            sm.ReleaseFrame();
                                                        
                            WriteToFile(paths.csvFile, landmarks);
                            landmarks = null;                            
                        }                        
                        sm.Dispose();
                        stopwatch.Stop();                        
                        //Update in realtime the current extraction
                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            elapsedLabel.Content = String.Format("Elapsed Time: {0} (s)", stopwatch.Elapsed.TotalSeconds.ToString("F"));
                        }));
                    }
                }
            }
        }

        void CreateThumbnail(string output_file, BitmapSource image)
        {            
            if (output_file != string.Empty)
            {
                using (FileStream stream = new FileStream(output_file, FileMode.Create))
                {
                    PngBitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    encoder.Save(stream);
                }
            }
        }

        private void WriteToFile(string csvFile, string landmarks)
        {
            using (StreamWriter sw = File.AppendText(csvFile))
            {               
                sw.Write(landmarks);
            }
        }

        //public List<string> GetOutputDirs(string output)
       // {
       //     return dirsOutput = new List<string>(System.IO.Directory.EnumerateDirectories(output).Where(x => x.Contains("Record")));
       // }

        /*     public Paths GetCurrentPaths(string dir)
             {
                 var streamFolderNames = new List<string> { "\\RGB", "\\Depth", "\\IR" };
                 var paths = new Paths
                 {
                     csvFile = Directory.GetFiles(dir, "*.csv")[0], //Create CSV File
                     rgbFolder = dir + streamFolderNames[0],
                     depthFolder = dir + streamFolderNames[1],
                     irFolder = dir + streamFolderNames[2]
                 };
                 return paths;
             }*/

        /*private Paths SetupOutput(string folderName)
        {
            var streamFolderNames = new List<string> { "\\RGB", "\\Depth", "\\IR" };
            char[] charSeparator = new char[] { '\\' };
            string[] stringSeparator = new string[] { "_@" };
            string recordName = folderName.Split(charSeparator).Last().Split(stringSeparator, StringSplitOptions.None).First();
            
            //recordName means Record_1, Record_2...
            string output = output_folder + "\\" + recordName;
            
            // Create the folder "Record_X" if it does not exist.There's no need to do an explicit check first
            //This folder will record all extracted frames and landmarks
            Directory.CreateDirectory(output); // C:\output\Record_X, where X = 1,2,3...

            var paths = new Paths
            {
                csvFile = CreateLandmarkHeader(output, recordName), //Create CSV File
                rgbFolder = output + streamFolderNames[0],
                depthFolder = output + streamFolderNames[1],
                irFolder = output + streamFolderNames[2]
            };

            //Create subfolders (RGB, Depth, IR) where images will be saved
            Directory.CreateDirectory(paths.rgbFolder);
            Directory.CreateDirectory(paths.depthFolder);
            Directory.CreateDirectory(paths.irFolder);

            /*foreach (var s in streamFolderNames) //Create subfolders (RGB, Depth, IR) where images will be saved
            {
                Directory.CreateDirectory(output + s);

                Debug.WriteLine(output + s);
            }            
            return paths;
        }*/

        public void SetupOutput()
        {
            var streamFolderNames = new List<string> { "\\RGB", "\\Depth", "\\IR" };
            char[] charSeparator = new char[] { '\\' };
            string[] stringSeparator = new string[] { "_@" };
            string recordName;
            string output = null;
            string videoName = null;
            var paths = new Paths();
            dictPaths.Clear();
            List<string> videosList = new List<string>();

            foreach (var dir in dirsSource)
            {
                //If the folder is not empty
                if (Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    videosList = new List<string>(Directory.GetFiles(dir, "*.rssdk"));
                    recordName = dir.Split(charSeparator).Last().Split(stringSeparator, StringSplitOptions.None).First();

                    //recordName means Record_1, Record_2...
                    output = output_folder + "\\" + recordName;

                    // Create the folder "Record_X" if it does not exist.There's no need to do an explicit check first
                    //This folder will record all extracted frames and landmarks
                    Directory.CreateDirectory(output); // C:\output\Record_X, where X = 1,2,3...

                    paths = new Paths
                    {
                        root = output,
                        csvFile = CreateLandmarkHeader(output, recordName), //Create CSV File
                        rgbFolder = output + streamFolderNames[0],
                        depthFolder = output + streamFolderNames[1],
                        irFolder = output + streamFolderNames[2]
                    };

                    //Create subfolders (RGB, Depth, IR) where images will be saved
                    Directory.CreateDirectory(paths.rgbFolder);
                    Directory.CreateDirectory(paths.depthFolder);
                    Directory.CreateDirectory(paths.irFolder);

                    foreach (var inputFile in videosList)
                    {
                        videoName = inputFile.Split('\\').Last().Split('.')[0];
                        Directory.CreateDirectory(paths.rgbFolder + "\\" + videoName);
                        Directory.CreateDirectory(paths.depthFolder + "\\" + videoName);
                        Directory.CreateDirectory(paths.irFolder + "\\" + videoName);
                    }
                }                
                dictPaths.Add(dir, paths);
            }            
        }

        private string CreateLandmarkHeader(string path, string fileName)
        {
            string landmarks = null;
            string file = path + "\\" + fileName + ".csv";
            
            using (StreamWriter fs = new StreamWriter(file, false))
            {
                //landmarks += "Video Index" + ";" + "User ID" + ";" + "Frame Index" + ";" + "Time Stamp" + ";"; // + "landmarkIndex" + ";" + "X" + ";" + "Y" + ";" + "Z" + '\n';
                landmarks += "Video Name" + ";" + "Frame Index" + ";" + "Frame RGB" + ";" + "Frame Depth" + ";" + "Frame IR" + ";" + "Time Stamp (100ns)" + ";" + "Average Distance to the Camera (mm)" + ";" + "Yaw" + ";" + "Pitch" + ";" + "Roll" + ";";

                for (int lm = 0; lm < 78; lm++) // Dataframe headers with WORLD Coordinates landmarks in meters 
                {
                    landmarks += "world_" + lm + "_X" + ";";
                    landmarks += "world_" + lm + "_Y" + ";";
                    landmarks += "world_" + lm + "_Z" + ";";
                }
                for (int lm = 0; lm < 78; lm++) // Dataframe headers with COLOR IMAGES landmarks 
                {
                    landmarks += "color_" + lm + "_X" + ";";
                    landmarks += "color_" + lm + "_Y" + ";";
                }
                landmarks += '\n';
                fs.Write(landmarks);
                landmarks = null;
            }
            return file;
        }

        private void sourceButton_Click(object sender, RoutedEventArgs e)
        {
            var folderBrowserDialog1 = new WinForms.FolderBrowserDialog();
            // Show the FolderBrowserDialog.
            WinForms.DialogResult result = folderBrowserDialog1.ShowDialog();
            if (result == WinForms.DialogResult.OK)
            {
                string folderName = folderBrowserDialog1.SelectedPath;
                textBox5.Text = input_folder = folderName;                
                dirsSource = new List<string>(System.IO.Directory.EnumerateDirectories(folderName).Where(x => x.Contains("Record")));
                outputButton.IsEnabled = true;
            }
        }

        private void OutputButton_Click(object sender, RoutedEventArgs e)
        {
            var folderBrowserDialog1 = new WinForms.FolderBrowserDialog();
            // Show the FolderBrowserDialog.
            WinForms.DialogResult result = folderBrowserDialog1.ShowDialog();
            if (result == WinForms.DialogResult.OK)
            {
                string folderName = folderBrowserDialog1.SelectedPath;
                textBox4.Text = output_folder = folderName;
                setupButton.IsEnabled = true;
            }
        }

        private void ExtractButton_Click(object sender, RoutedEventArgs e)
        {            
            if (input_folder == null || output_folder == null)// || !imageStreams.Any())
            {
                string message = "Please, select the Source and Output directories!";
                string caption = "Missing root folder";
                WinForms.MessageBoxButtons buttons = WinForms.MessageBoxButtons.OK;
                WinForms.DialogResult result;
                // Displays the MessageBox.
                result = WinForms.MessageBox.Show(message, caption, buttons);
            }
            else
            {
                processingThread = new Thread(new ThreadStart(ProcessingThread));
                processingThread.Start();
                stopButton.IsEnabled = true;
                ExtractButton.IsEnabled = false;
            }
        }

        private void stopButton_Click(object sender, RoutedEventArgs e)
        {
            string message = "Are you sure to abort the database extraction?\n You will need to start from beginning if you stop now!";
            string caption = "Stop Extraction";
            WinForms.MessageBoxButtons buttons = WinForms.MessageBoxButtons.YesNo;
            WinForms.DialogResult result;
            // Displays the MessageBox.
            result = WinForms.MessageBox.Show(message, caption, buttons);

            if (result == WinForms.DialogResult.Yes)
            {
                processingThread.Abort();

                stopButton.IsEnabled = false;
                ExtractButton.IsEnabled = false;
                sourceButton.IsEnabled = true;
                outputButton.IsEnabled = true;
                setupButton.IsEnabled = true;
            }
        }

        private void SetupButton_Click(object sender, RoutedEventArgs e)
        {
            string message = String.Format("Database output structure will be created in {0}", output_folder);
            string caption = "Output Setup";
            WinForms.MessageBoxButtons buttons = WinForms.MessageBoxButtons.OKCancel;
            WinForms.DialogResult result;
            result = WinForms.MessageBox.Show(message, caption, buttons);
            if (result == WinForms.DialogResult.OK)
            {
                sourceButton.IsEnabled = false;
                outputButton.IsEnabled = false;
                textBox1.Text = textBox2.Text = textBox3.Text = null;

                SetupOutput();
                GenerateIndexLabels();
                result = WinForms.MessageBox.Show("Output Structure created succesfully!\n" +
                    "Now you can Extract the frames and landmarks :)\n" +
                    "Please, click on 'Create Database' Button",
                    "Output Setup", WinForms.MessageBoxButtons.OK);
                Process.Start(output_folder);
                
                ExtractButton.IsEnabled = true;
                setupButton.IsEnabled = false;
            }
        }

        private void GenerateIndexLabels()
        {
            Record record = new Record();
            VideosCollection videos = new VideosCollection();
            string indexFile = output_folder + "\\" + "Index.csv";
            string surveyFile = null;
            string stringTemp = null;
            string videoName = null;

            stringTemp += "Record" + ";" + "Video Name" + ";" + "Path - RGB Frames" + ";" + "Path - Depth Frames" + ";" + "Path - IR Frames" + ";" + "Q1" + ";" + "Q2" + ";" + "Q3" + ";" + "Q4" + ";" + "Q5" + ";" + "Q6" + ";" + "\n";
            
            using (StreamWriter sw = new StreamWriter(indexFile, false, Encoding.GetEncoding("ISO-8859-1")))
            {
                foreach (var d in dictPaths)
                {
                    surveyFile = Directory.GetFiles(d.Key, "Survey.txt")[0];
                    videos = LoadSurveyJson(surveyFile);
                    
                    foreach (var v in videos.Videos)
                    {
                        videoName = v.VideoName.Split('\\').Last();//.Split('.')[0];
                        stringTemp += d.Value.root + ";" + videoName + ";" + d.Value.rgbFolder + "\\" + videoName.Split('.')[0] + ";"
                            + d.Value.depthFolder + "\\" + videoName.Split('.')[0] + ";" + d.Value.irFolder + "\\" + videoName.Split('.')[0] + ";";
                        foreach (var a in v.Answers)
                            stringTemp += a.Answer + ";";
                        stringTemp += "\n";
                    }                
                    sw.Write(stringTemp);
                    stringTemp = null;
                }
            }
        }

        public Record LoadRecordJson(string filename)
        {
            Record record = JsonConvert.DeserializeObject<Record>(File.ReadAllText(filename));
            return record;
        }
        public VideosCollection LoadSurveyJson(string filename)
        {
            VideosCollection videos = JsonConvert.DeserializeObject<VideosCollection>(File.ReadAllText(filename));
            return videos;
        }    
    }
}