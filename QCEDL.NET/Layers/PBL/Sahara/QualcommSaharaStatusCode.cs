namespace Qualcomm.EmergencyDownload.Layers.PBL.Sahara;

internal enum QualcommSaharaStatusCode : uint
{
    /* Success */
    StatusSuccess = 0x00,

    /* Invalid command received in current state */
    ErrorInvalidCmd = 0x01,

    /* Protocol mismatch between host and target */
    ErrorProtocolMismatch = 0x02,

    /* Invalid target protocol version */
    ErrorInvalidTargetProtocol = 0x03,

    /* Invalid host protocol version */
    ErrorInvalidHostProtocol = 0x04,

    /* Invalid packet size received */
    ErrorInvalidPacketSize = 0x05,

    /* Unexpected image ID received */
    ErrorUnexpectedImageId = 0x06,

    /* Invalid image header size received */
    ErrorInvalidHeaderSize = 0x07,

    /* Invalid image data size received */
    ErrorInvalidDataSize = 0x08,

    /* Invalid image type received */
    ErrorInvalidImageType = 0x09,

    /* Invalid tranmission length */
    ErrorInvalidTxLength = 0x0A,

    /* Invalid reception length */
    ErrorInvalidRxLength = 0x0B,

    /* General transmission or reception error */
    ErrorGeneralTxRxError = 0x0C,

    /* Error while transmitting READ_DATA packet */
    ErrorReadDataError = 0x0D,

    /* Cannot receive specified number of program headers */
    ErrorUnsupportedNumPhdrs = 0x0E,

    /* Invalid data length received for program headers */
    ErrorInvalidPdhrSize = 0x0F,

    /* Multiple shared segments found in ELF image */
    ErrorMultipleSharedSeg = 0x10,

    /* Uninitialized program header location */
    ErrorUninitPhdrLoc = 0x11,

    /* Invalid destination address */
    ErrorInvalidDestAddr = 0x12,

    /* Invalid data size receieved in image header */
    ErrorInvalidImgHdrDataSize = 0x13,

    /* Invalid ELF header received */
    ErrorInvalidElfHdr = 0x14,

    /* Unknown host error received in HELLO_RESP */
    ErrorUnknownHostError = 0x15,

    /* Timeout while receiving data */
    ErrorTimeoutRx = 0x16,

    /* Timeout while transmitting data */
    ErrorTimeoutTx = 0x17,

    /* Invalid mode received from host */
    ErrorInvalidHostMode = 0x18,

    /* Invalid memory read access */
    ErrorInvalidMemoryRead = 0x19,

    /* Host cannot handle read data size requested */
    ErrorInvalidDataSizeRequest = 0x1A,

    /* Memory debug not supported */
    ErrorMemoryDebugNotSupported = 0x1B,

    /* Invalid mode switch */
    ErrorInvalidModeSwitch = 0x1C,

    /* Failed to execute command */
    ErrorCmdExecFailure = 0x1D,

    /* Invalid parameter passed to command execution */
    ErrorExecCmdInvalidParam = 0x1E,

    /* Unsupported client command received */
    ErrorExecCmdUnsupported = 0x1F,

    /* Invalid client command received for data response */
    ErrorExecDataInvalidClientCmd = 0x20,

    /* Failed to authenticate hash table */
    ErrorHashTableAuthFailure = 0x21,

    /* Failed to verify hash for a given segment of ELF image */
    ErrorHashVerificationFailure = 0x22,

    /* Failed to find hash table in ELF image */
    ErrorHashTableNotFound = 0x23,

    /* Target failed to initialize */
    ErrorTargetInitFailure = 0x24,

    /* Failed to authenticate generic image */
    ErrorImageAuthFailure = 0x25,

    /* Invalid ELF hash table size.  Too bit or small. */
    ErrorInvalidImgHashTableSize = 0x26
}