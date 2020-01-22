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
        private string landmarks = null;

        private string input_folder = null;
        private string output_folder = null;
        private string output_file = null;
        private IEnumerable<CheckBox> imageStreams;

        List<string> dirs;        

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
            //int lostFrames = 0;
            int frameIndex = 0;
            int nframes = 0;
            PXCMImage color;
            PXCMImage depth;
            PXCMImage ir;
            PXCMImage.ImageData imageColor;
            PXCMImage.ImageData imageDepth;
            PXCMImage.ImageData imageIr;
            WriteableBitmap wbm1, wbm2, wbm3;

            //For each directory, extract all landmarks or images streams from all videos
            foreach (var dir in dirs)
            {
                Debug.WriteLine(dir);
                //If the folder is not empty
                if (Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    List<string> fileList = new List<string>(Directory.GetFiles(dir, "*.rssdk"));
                    foreach (var l in fileList)
                    {
                        Debug.WriteLine(l);
                    }
                    WriteToFile(dir);
                }
            }
        }

        private void WriteToFile(string folderName)
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
                    Debug.WriteLine("entramos do write else");
                    //landmarks += "Video Index" + ";" + "User ID" + ";" + "Frame Index" + ";" + "Time Stamp" + ";"; // + "landmarkIndex" + ";" + "X" + ";" + "Y" + ";" + "Z" + '\n';
                    landmarks += "Video Name" + ";" + "Frame RGB" + ";" + "Frame Depth" + ";" + "Frame IR" + ";" + "Frame Index" + ";" + "Time Stamp (100ns)" + ";"; // + "landmarkIndex" + ";" + "X" + ";" + "Y" + ";" + "Z" + '\n';

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
        }

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
                dirs = new List<string>(System.IO.Directory.EnumerateDirectories(folderName));
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
