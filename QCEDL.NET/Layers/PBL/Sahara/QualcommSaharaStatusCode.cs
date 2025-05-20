namespace Qualcomm.EmergencyDownload.Layers.PBL.Sahara
{
    internal enum QualcommSaharaStatusCode : uint
    {
        /* Success */
        STATUS_SUCCESS = 0x00,

        /* Invalid command received in current state */
        ERROR_INVALID_CMD = 0x01,

        /* Protocol mismatch between host and target */
        ERROR_PROTOCOL_MISMATCH = 0x02,

        /* Invalid target protocol version */
        ERROR_INVALID_TARGET_PROTOCOL = 0x03,

        /* Invalid host protocol version */
        ERROR_INVALID_HOST_PROTOCOL = 0x04,

        /* Invalid packet size received */
        ERROR_INVALID_PACKET_SIZE = 0x05,

        /* Unexpected image ID received */
        ERROR_UNEXPECTED_IMAGE_ID = 0x06,

        /* Invalid image header size received */
        ERROR_INVALID_HEADER_SIZE = 0x07,

        /* Invalid image data size received */
        ERROR_INVALID_DATA_SIZE = 0x08,

        /* Invalid image type received */
        ERROR_INVALID_IMAGE_TYPE = 0x09,

        /* Invalid tranmission length */
        ERROR_INVALID_TX_LENGTH = 0x0A,

        /* Invalid reception length */
        ERROR_INVALID_RX_LENGTH = 0x0B,

        /* General transmission or reception error */
        ERROR_GENERAL_TX_RX_ERROR = 0x0C,

        /* Error while transmitting READ_DATA packet */
        ERROR_READ_DATA_ERROR = 0x0D,

        /* Cannot receive specified number of program headers */
        ERROR_UNSUPPORTED_NUM_PHDRS = 0x0E,

        /* Invalid data length received for program headers */
        ERROR_INVALID_PDHR_SIZE = 0x0F,

        /* Multiple shared segments found in ELF image */
        ERROR_MULTIPLE_SHARED_SEG = 0x10,

        /* Uninitialized program header location */
        ERROR_UNINIT_PHDR_LOC = 0x11,

        /* Invalid destination address */
        ERROR_INVALID_DEST_ADDR = 0x12,

        /* Invalid data size receieved in image header */
        ERROR_INVALID_IMG_HDR_DATA_SIZE = 0x13,

        /* Invalid ELF header received */
        ERROR_INVALID_ELF_HDR = 0x14,

        /* Unknown host error received in HELLO_RESP */
        ERROR_UNKNOWN_HOST_ERROR = 0x15,

        /* Timeout while receiving data */
        ERROR_TIMEOUT_RX = 0x16,

        /* Timeout while transmitting data */
        ERROR_TIMEOUT_TX = 0x17,

        /* Invalid mode received from host */
        ERROR_INVALID_HOST_MODE = 0x18,

        /* Invalid memory read access */
        ERROR_INVALID_MEMORY_READ = 0x19,

        /* Host cannot handle read data size requested */
        ERROR_INVALID_DATA_SIZE_REQUEST = 0x1A,

        /* Memory debug not supported */
        ERROR_MEMORY_DEBUG_NOT_SUPPORTED = 0x1B,

        /* Invalid mode switch */
        ERROR_INVALID_MODE_SWITCH = 0x1C,

        /* Failed to execute command */
        ERROR_CMD_EXEC_FAILURE = 0x1D,

        /* Invalid parameter passed to command execution */
        ERROR_EXEC_CMD_INVALID_PARAM = 0x1E,

        /* Unsupported client command received */
        ERROR_EXEC_CMD_UNSUPPORTED = 0x1F,

        /* Invalid client command received for data response */
        ERROR_EXEC_DATA_INVALID_CLIENT_CMD = 0x20,

        /* Failed to authenticate hash table */
        ERROR_HASH_TABLE_AUTH_FAILURE = 0x21,

        /* Failed to verify hash for a given segment of ELF image */
        ERROR_HASH_VERIFICATION_FAILURE = 0x22,

        /* Failed to find hash table in ELF image */
        ERROR_HASH_TABLE_NOT_FOUND = 0x23,

        /* Target failed to initialize */
        ERROR_TARGET_INIT_FAILURE = 0x24,

        /* Failed to authenticate generic image */
        ERROR_IMAGE_AUTH_FAILURE = 0x25,

        /* Invalid ELF hash table size.  Too bit or small. */
        ERROR_INVALID_IMG_HASH_TABLE_SIZE = 0x26
    };
}
