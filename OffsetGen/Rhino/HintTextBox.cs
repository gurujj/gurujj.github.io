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
    // 비어 있을 때 흐린 글씨로 예제를 보여주는 텍스트 상자
    // ---------------------------------------------------------------
    public class HintTextBox : TextBox
    {
        const int WM_PAINT = 0x000F;

        public string Hint { get; set; }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WM_PAINT && Text.Length == 0 && !string.IsNullOrEmpty(Hint))
            {
                using (var g = Graphics.FromHwnd(Handle))
                {
                    var rect = new Rectangle(1, 1, Math.Max(1, ClientSize.Width - 1), Math.Max(1, ClientSize.Height - 1));
                    TextRenderer.DrawText(g, Hint, Font, rect, SystemColors.GrayText,
                        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                }
            }
        }
    }
}
