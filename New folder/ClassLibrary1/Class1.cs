using System;

namespace ddd
{
    public class Class1
    {
        static void Main(string[] args)
        {
            StreamWriter sw = new StreamWriter("D:\\QXT\\sampleCode\\Pairs_production\\newTxt.txt",false);
           
            sw.WriteLine("Hwllo");
            sw.Close();

            
        }

    }


}



