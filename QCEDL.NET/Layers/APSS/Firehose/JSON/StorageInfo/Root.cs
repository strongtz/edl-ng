namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo
{
    public class Root
    {
        public StorageInfo storage_info
        {
            get; set;
        }

        public Root() // Add parameterless constructor
        {
            storage_info = new StorageInfo(); // Initialize, or allow null
        }
    }
}
