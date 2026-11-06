using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input.Custom;

namespace OffsetGenPlugin
{
    // ---------------------------------------------------------------
    // 플러그인 본체
    // ---------------------------------------------------------------
    public class OffsetGenPlugIn : Rhino.PlugIns.PlugIn
    {
        public static OffsetGenPlugIn Instance { get; private set; }
        public OffsetGenPlugIn() { Instance = this; }
    }
}
