using System.Collections.Generic;

namespace EGG9000.Common.Helpers.ArtifactImaging {
    // Describes where each artifact and stone sits inside a generated image so the site can lay a grid
    // of invisible hover targets on top of it. Rects are stored as percentages of the image size (0-100)
    // rather than pixels: the image scales responsively in the browser, and percentage hotspots scale
    // right along with it without any extra math on the client.
    public sealed class ArtifactOverlayManifest {
        // Native pixel size of the image the hotspots were measured against. Handy for aspect-ratio
        // boxes on the client; the hotspots themselves are already normalised.
        public int Width { get; set; }
        public int Height { get; set; }
        public List<ArtifactHotspot> Hotspots { get; set; } = new();
    }

    // One hover target: the full cell of an artifact. Its tooltip lists the artifact plus any slotted
    // stones, so stones don't need their own hotspots.
    public sealed class ArtifactHotspot {
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
        // Rich tooltip markup (already HTML-encoded where needed): name, rarity, effect, stone lines.
        public string Tip { get; set; }
    }
}
