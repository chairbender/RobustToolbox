using System;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Client.Graphics.Drawing;

namespace Robust.Client.Placement.Modes
{
    public class SnapgridCenter : PlacementMode
    {
        bool onGrid;
        float snapSize;

        public override bool HasLineMode => true;
        public override bool HasGridMode => true;

        public SnapgridCenter(PlacementManager pMan) : base(pMan) { }

        public override void Render(DrawingHandleWorld handle)
        {
            if (onGrid)
            {
                var viewportSize = (Vector2)pManager._clyde.ScreenSize;
                var position = pManager.eyeManager.ScreenToWorld(Vector2.Zero);
                var gridstart = pManager.eyeManager.WorldToScreen(new Vector2( //Find snap grid closest to screen origin and convert back to screen coords
                    (float)(Math.Round(position.X / snapSize - 0.5f, MidpointRounding.AwayFromZero) + 0.5f) * snapSize,
                    (float)(Math.Round(position.Y / snapSize - 0.5f, MidpointRounding.AwayFromZero) + 0.5f) * snapSize));
                for (var a = gridstart.X; a < viewportSize.X; a += snapSize * 32) //Iterate through screen creating gridlines
                {
                    var from = ScreenToWorld(new Vector2(a, 0));
                    var to = ScreenToWorld(new Vector2(a, viewportSize.Y));
                    handle.DrawLine(from, to, new Color(0, 0, 1f));
                }
                for (var a = gridstart.Y; a < viewportSize.Y; a += snapSize * 32)
                {
                    var from = ScreenToWorld(new Vector2(0, a));
                    var to = ScreenToWorld(new Vector2(viewportSize.X, a));
                    handle.DrawLine(from, to, new Color(0, 0, 1f));
                }
            }

            // Draw grid BELOW the ghost thing.
            base.Render(handle);
        }

        public override void AlignPlacementMode(ScreenCoordinates mouseScreen)
        {
            MouseCoords = ScreenToCursorGrid(mouseScreen);

            var grid = pManager.MapManager.GetGrid(MouseCoords.GridID);

            snapSize = grid.SnapSize; //Find snap size.
            GridDistancing = snapSize;
            onGrid = true;

            var snapped = grid.SnapGridCenter(MouseCoords);

            //Adjust mouseCoords to new calculated position
            MouseCoords = snapped.Translated(new Vector2(pManager.PlacementOffset.X, pManager.PlacementOffset.Y));
        }

        public override bool IsValidPosition(GridCoordinates position)
        {
            if (!RangeCheck(position))
            {
                return false;
            }

            return true;
        }
    }
}
