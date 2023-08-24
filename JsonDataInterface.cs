using System.Text;
using TwinCAT.Ads;

namespace AdsStressTester
{
    internal class JsonDataInterface
    {
        private int _defaultBitValue = 30000;

        // Access via JSON Data Interface https://infosys.beckhoff.com/english.php?content=../content/1033/tf6020_tc3_json_data_interface/10821785483.html&id=  
        public string getData(string json)
        {
            int adsPort = 851;
            string amsNetId = "10.0.0.170.1.1";
            string responseString;

            using (AdsClient adsClient = new AdsClient())
            {
                adsClient.Connect(amsNetId, adsPort);
                byte[] writeData = new byte[json.Length + 1];
                MemoryStream writeStream = new MemoryStream(writeData);
                BinaryWriter writer = new BinaryWriter(writeStream);
                writer.Write(Encoding.ASCII.GetBytes(json));

                byte[] readData = new byte[_defaultBitValue];

                adsClient.ReadWrite(0xf070, 0, readData, writeData);

                responseString = Encoding.ASCII.GetString(readData);

                writeStream.Dispose();
                writer.Dispose();
                return responseString;
            }
        }
    }
}
