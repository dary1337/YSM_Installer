using System.Drawing;
using System.Windows.Forms;

namespace YSMInstaller {
    /// <summary>Rounded Material 3 surface container with optional hairline outline.</summary>
    public class MaterialCard : RoundedPanel {
        public MaterialCard(int cornerRadius = Sizes.RadiusMedium)
            : base(cornerRadius) {
            BackColor = MaterialPalette.SurfaceContainer;
            Padding = new Padding(16);
            SetOutline(MaterialPalette.OutlineVariant);
        }
    }
}
