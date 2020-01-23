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
//using System.Windows.Forms;
//using Control = System.Windows.Controls.Control;

namespace Video_Survey_Database_Extractor
{
    /// <summary>
    /// Interação lógica para MainWindow.xam
    /// </summary>
    public partial class MainWindow : Window
    {
        private PXCMSenseManager sm;
        private Thread processingThread;
        //private string landmarks = null;

        private string input_folder = null;
        private string output_folder = null;
        private string output_file = null;
        private IEnumerable<CheckBox> imageStreams;

        List<string> dirsSource;        

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            processingThread.Abort();
            sm.Dispose();
        }

        private void ProcessingThread()
        {
            string nameColor, nameDepth, nameIr, file, folder;
            int width = 640;
            int height = 480;
            
            int frameIndex = 0;
            int nframes = 0;
            int lostFrames = 0;
            string landmarks = null;
            long frameTimeStamp = 0;
            PXCMImage color;
            PXCMImage depth;
            PXCMImage ir;
            PXCMImage.ImageData imageColor;
            PXCMImage.ImageData imageDepth;
            PXCMImage.ImageData imageIr;
            WriteableBitmap wbm1, wbm2, wbm3;

            PXCMFaceModule faceModule;
            PXCMFaceConfiguration faceConfig;
            PXCMFaceData faceData = null;

            //For each directory, extract all landmarks or images streams from all videos
            foreach (var dir in dirsSource)
            {
                //If the folder is not empty
                if (Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    //WriteToFile(dir);
                    Paths paths = SetupOutput(dir);
                    List<string> fileList = new List<string>(Directory.GetFiles(dir, "*.rssdk"));

                    foreach (var inputFile in fileList)
                    {
                        lostFrames = 0;
                        Debug.WriteLine(inputFile);
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
                            textBox3.Text = inputFile;
                        }));

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
                        while (sm.AcquireFrame(true) >= pxcmStatus.PXCM_STATUS_NO_ERROR)
                        {
                            // Retrieve face data
                            faceModule = sm.QueryFace();
                            if (faceModule != null)
                            {
                                // Retrieve the most recent processed data
                                faceData = faceModule.CreateOutput();
                                faceData.Update();
                            }
                            if (faceData != null)
                            {
                                Int32 nfaces = faceData.QueryNumberOfDetectedFaces();
                                frameIndex = sm.captureManager.QueryFrameIndex();
                                frameTimeStamp = sm.captureManager.QueryFrameTimeStamp();
                                //PXCMCapture.Sample sample = senseManager.QuerySample();
                                if (nfaces == 0) //If none face was detected, we will consider as a "lost frame"
                                {
                                    lostFrames += 1;
                                }
                                for (Int32 i = 0; i < nfaces; i++)
                                {
                                    PXCMFaceData.Face face = faceData.QueryFaceByIndex(i);
                                    PXCMFaceData.LandmarksData landmarkData = face.QueryLandmarks();

                                    //var point3 = new PXCMPoint3DF32(); ????
                                    if (landmarkData != null)
                                    {
                                        PXCMFaceData.LandmarkPoint[] landmarkPoints;
                                        landmarkData.QueryPoints(out landmarkPoints);

                                        Application.Current.Dispatcher.BeginInvoke(new Action(() => textBox1.Text = frameIndex.ToString()));
                                        //Falta colocar os paths das imagens
                                        landmarks += inputFile.Split('\\').Last() + ";" + ";" + ";" + ";" + frameIndex + ";" + frameTimeStamp + ";"; // Begin line with frame info

                                        for (int j = 0; j < landmarkPoints.Length; j++) // Writes landmarks coordinates along the line 
                                        {
                                            //get world coordinates
                                            landmarks += landmarkPoints[j].world.x.ToString() + ";" + landmarkPoints[j].world.y.ToString() + ";" + landmarkPoints[j].world.z.ToString() + ";";
                                            //get coordinate of the image pixel
                                            landmarks += landmarkPoints[j].image.x.ToString() + ";" + landmarkPoints[j].image.y.ToString() + ";";
                                           /* if (j % 100 == 0)//DEBUGAR O J
                                            {
                                                WriteToFile(paths.csvFile, landmarks); // After 100 frames, writes to file and empties landmarks string
                                                landmarks = null;
                                            }*/
                                        } // DEBUGAR INDEX DO LANDMARK landmarkPoints[j].source.ALIAS E INDEX E CONFIANÇA
                                        landmarks += '\n'; // Breaks line after the end of the frame coordinates
                                        WriteToFile(paths.csvFile, landmarks); // After 100 frames, writes to file and empties landmarks string
                                        landmarks = null;//TENTAR SALVAR MAIS DE UM FRAME POR VEZ, DIMINUIR ESCRITA EM DISCO
                                    }
                                }
                            }
                            // Release the frame
                            if (faceData != null) faceData.Dispose();
                            sm.ReleaseFrame();
                        }
                        sm.Dispose();
                    }
                }
            }
        }

        private void WriteToFile(string csvFile, string landmarks)
        {
            //output_file = output_folder + "\\" + folderName.Split('\\').Last() + ".csv";

            if (File.Exists(csvFile))//Append more data
            //{
                using (System.IO.StreamWriter sw = File.AppendText(csvFile))
                {
                    //get world and image coordinates
                    sw.Write(landmarks);
                }
                //landmarks = null;
           // }
        }

        struct Paths
        {
            public string csvFile;
            public string rgbFolder;
            public string depthFolder;
            public string irFolder;
        }

        private Paths SetupOutput(string folderName)
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
            }*/            
            return paths;
        }

        private string CreateLandmarkHeader(string path, string fileName)
        {
            string landmarks = null;
            string file = path + "\\" + fileName + ".csv";
            if (!File.Exists(file))
            {
                using (System.IO.StreamWriter fs = File.AppendText(file))
                {                    
                    //landmarks += "Video Index" + ";" + "User ID" + ";" + "Frame Index" + ";" + "Time Stamp" + ";"; // + "landmarkIndex" + ";" + "X" + ";" + "Y" + ";" + "Z" + '\n';
                    landmarks += "Video Name" + ";" + "Frame Index" + ";" + "Frame RGB" + ";" + "Frame Depth" + ";" + "Frame IR" + ";" + "Time Stamp (100ns)" + ";"; // + "landmarkIndex" + ";" + "X" + ";" + "Y" + ";" + "Z" + '\n';

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
                        //landmarks += "color_" + lm + "_Z" + ";";
                    }
                    landmarks += '\n';
                    fs.Write(landmarks);
                    landmarks = null;
                }
            }
            return file;
        }

        /*private void WriteToFile(string folderName)
        {
            output_file = output_folder + "\\" + folderName.Split('\\').Last() + ".csv";

            if (File.Exists(output_file))//Append more data
            {
                using (System.IO.StreamWriter sw = File.AppendText(output_file))
                {
                    //get world coordinate
                    sw.Write(landmarks);
                }
                landmarks = null;
            }
            else // Writes header on the beginning of the file
            {
                using (System.IO.StreamWriter fs = File.AppendText(output_file))
                {
                    //Debug.WriteLine("entramos do write else");
                    //landmarks += "Video Index" + ";" + "User ID" + ";" + "Frame Index" + ";" + "Time Stamp" + ";"; // + "landmarkIndex" + ";" + "X" + ";" + "Y" + ";" + "Z" + '\n';
                    landmarks += "Video Name" + ";" + "Frame Index" + ";" + "Frame RGB" + ";" + "Frame Depth" + ";" + "Frame IR" + ";" + "Time Stamp (100ns)" + ";"; // + "landmarkIndex" + ";" + "X" + ";" + "Y" + ";" + "Z" + '\n';

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
                        landmarks += "color_" + lm + "_Z" + ";";
                    }
                    landmarks += '\n';
                    fs.Write(landmarks);
                    landmarks = null;
                }
            }
        }*/

        private void sourceButton_Click(object sender, RoutedEventArgs e)
        {
            var folderBrowserDialog1 = new WinForms.FolderBrowserDialog();
            // Show the FolderBrowserDialog.
            WinForms.DialogResult result = folderBrowserDialog1.ShowDialog();
            if (result == WinForms.DialogResult.OK)
            {
                string folderName = folderBrowserDialog1.SelectedPath;
                textBox5.Text = folderName;
                input_folder = folderName;

                dirsSource = new List<string>(System.IO.Directory.EnumerateDirectories(folderName).Where(x => x.Contains("Record_")));
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
                textBox4.Text = folderName;
                output_folder = folderName;
            }
        }

        private void ExtractButton_Click(object sender, RoutedEventArgs e)
        {
            var checkboxName = new List<string> { "rgbCheckBox", "depthCheckBox", "irCheckBox" };
            imageStreams = (this.Content as Panel).Children.OfType<CheckBox>().Where(x => x.IsChecked == true && checkboxName.Contains(x.Name));
            foreach (CheckBox c in imageStreams)
            { Debug.WriteLine(c.Name); }

            if (input_folder == null || output_folder == null || !imageStreams.Any())
            {
                string message = "Please, select the Source and Output directories!\n Choose at least one kind of Image Stream!";
                string caption = "Missing root folder or Image Stream";
                WinForms.MessageBoxButtons buttons = WinForms.MessageBoxButtons.OK;
                WinForms.DialogResult result;
                // Displays the MessageBox.
                result = WinForms.MessageBox.Show(message, caption, buttons);
            }
            else
            {
                Debug.WriteLine("entrou no else");
                processingThread = new Thread(new ThreadStart(ProcessingThread));
                processingThread.Start();
            }
        }
    }
}
