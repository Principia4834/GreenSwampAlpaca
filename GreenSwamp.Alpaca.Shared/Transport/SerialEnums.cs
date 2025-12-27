using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GreenSwamp.Alpaca.Shared.Transport
{
    public enum SerialSpeed
    {
        ps300 = 300, // 0x0000012C
        ps1200 = 1200, // 0x000004B0
        ps2400 = 2400, // 0x00000960
        ps4800 = 4800, // 0x000012C0
        ps9600 = 9600, // 0x00002580
        ps14400 = 14400, // 0x00003840
        ps19200 = 19200, // 0x00004B00
        ps28800 = 28800, // 0x00007080
        ps38400 = 38400, // 0x00009600
        ps57600 = 57600, // 0x0000E100
        ps115200 = 115200, // 0x0001C200
        ps230400 = 230400, // 0x00038400
    }

}
