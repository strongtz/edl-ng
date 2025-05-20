namespace Qualcomm.EmergencyDownload.Layers.PBL.Sahara.Data
{
    internal struct QualcommSaharaHelloResponse
    {
        public QualcommSaharaHeader Header;
        public uint Version;
        public uint VersionSupported;
        public uint Status;
        public uint Mode;
        public uint Reserved0;
        public uint Reserved1;
        public uint Reserved2;
        public uint Reserved3;
        public uint Reserved4;
        public uint Reserved5;
    }
}
