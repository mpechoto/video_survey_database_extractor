using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Video_Survey_Database_Extractor
{
    public class Record
    {
        public string Name { get; set; }
        public decimal Age { get; set; }
        public string Gender { get; set; }
        public List<string> Videos { get; set; }
    }

    public class VideosCollection
    {
        public ICollection<Video> Videos { get; set; }
    }

    public class Video
    {
        public string VideoName { get; set; }
        public ICollection<Answers> Answers { get; set; }
    }

    public class Answers
    {
        public int Id { get; set; }
        public string Answer { get; set; }
    }
           
    public struct Paths
    {
        public string root;
        public string csvFile;
        public string rgbFolder;
        public string depthFolder;
        public string irFolder;
    }

    public struct Offset
    {
        public int x, y, w, h;
        public Offset(int x, int y, int w, int h)
        {
            this.x = x;
            this.y = y;
            this.w = w;
            this.h = h;
        }
    }
}
