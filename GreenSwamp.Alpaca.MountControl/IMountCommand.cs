using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GreenSwamp.Alpaca.MountControl
{
    public interface IMountCommand
    {
        long Id { get; }
        DateTime CreatedUtc { get; }
        bool Successful { get; set; }
        Exception Exception { get; set; }
        dynamic Result { get; }
        void Execute(SkyWatcher skyWatcher);
    }
}
