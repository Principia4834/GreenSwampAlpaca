using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GreenSwamp.Alpaca.Shared
{
    public enum SerialParity
    {
        None = 0,
        Odd = 1,
        Even = 2,
        Mark = 3,
        Space = 4
    }

    public enum SerialStopBits
    {
        None = 0,
        One = 1,
        OnePointFive = 3,
        Two = 2
    }

    public enum SerialFlowControl
    {
        None = 0,
        XonXoff = 1,
        RtsCts = 2,
        RtsXonXoff = 3
    }

    public enum SerialSpeed
    {
        Baud300 = 300,
        Baud1200 = 1200,
        Baud2400 = 2400,
        Baud4800 = 4800,
        Baud9600 = 9600,
        Baud14400 = 14400,
        Baud19200 = 19200,
        Baud38400 = 38400,
        Baud57600 = 57600,
        Baud115200 = 115200,
        Baud230400 = 230400
    }
}
