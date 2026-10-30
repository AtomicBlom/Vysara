using System.Numerics;
using SUIsei.Composition;
using SUIsei.Controls;
using SUIsei.Drawing;
using SUIsei.Media;

namespace SUIsei.Tests;

/// <summary>
/// A compositor other than <see cref="DrawListCompositor"/> is a full peer at the <see cref="IUICompositor"/> boundary:
/// it finds what changed through <see cref="Visual.Version"/>, which nothing clears, and its lists reach
/// <see cref="UIHost.DrawLists"/> through <see cref="IDrawListSource"/>.
/// </summary>
public sealed class ForeignCompositorTests
{
	private static readonly HdrColor Red = HdrColor.FromRgb(220, 40, 40);
	private static readonly HdrColor Blue = HdrColor.FromRgb(40, 40, 220);

	[Test]
	public void A_foreign_compositors_list_reaches_the_host_and_it_sees_each_change_through_the_version()
	{
		var brush = new SolidColorBrush(Red);
		var box = new Border { Width = 40f, Height = 20f, Background = brush, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
		var compositor = new QuadCompositor();
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default, compositor: compositor);
		view.Root.Content = box;

		host.Tick(0, 0f, new Vector2(100f, 100f));
		host.Render();
		var (id, list) = host.DrawLists.ShouldHaveSingleItem();
		id.ShouldBe(view.Surface);
		list.ShouldBeSameAs(compositor.LastDraw(view.Surface));
		list.Commands.Length.ShouldBe(1);
		list.Vertices.Span[0].Color.ShouldBe(Red.ToPremultiplied());
		list.Vertices.Span[2].Position.ShouldBe(new Vector2(40f, 20f));
		list.TextureSlots.ShouldBe([TextureSource.White]);
		compositor.ChangedVisuals.ShouldBeGreaterThan(0);

		// Nothing changed: every version is the one it composited, although IsDirty, which only DrawListCompositor
		// clears, still reads true.
		host.Tick(0.1, 0.1f, new Vector2(100f, 100f));
		host.Render();
		compositor.ChangedVisuals.ShouldBe(0);
		box.Visual.IsDirty.ShouldBeTrue();
		var again = host.DrawLists[0].Data;
		again.Revision.ShouldBe(list.Revision);
		again.Damage.IsEmpty.ShouldBeTrue();

		// A recolour writes no visual property, but marks the box's visual and so moves its version.
		var before = box.Visual.Version;
		brush.Color = Blue;
		box.Visual.Version.ShouldNotBe(before);
		host.Tick(0.2, 0.1f, new Vector2(100f, 100f));
		host.Render();
		compositor.ChangedVisuals.ShouldBe(1);
		var recoloured = host.DrawLists[0].Data;
		recoloured.Revision.ShouldBeGreaterThan(list.Revision);
		recoloured.Vertices.Span[0].Color.ShouldBe(Blue.ToPremultiplied());
		recoloured.Damage.IsEmpty.ShouldBeFalse();
	}

	// Draws each rect filled with a colour as one quad, rebuilding its list only when a visual's version has moved
	// since the composite before.
	private sealed class QuadCompositor : IUICompositor, IDrawListSource
	{
		private readonly Dictionary<int, Built> _lists = [];
		private readonly Dictionary<Visual, uint> _seen = new(ReferenceEqualityComparer.Instance);
		private uint _revision;

		/// <summary>How many visuals the last composite found changed.</summary>
		public int ChangedVisuals { get; private set; }

		public void Composite(RootVisual root, DrawListId surface)
		{
			ChangedVisuals = 0;
			Changes(root);
			if (_lists.TryGetValue(surface.Value, out var built) && ChangedVisuals == 0)
			{
				// The same list again: its twin with no damage, made with it, so a clean frame allocates nothing.
				_lists[surface.Value] = built with { Fresh = false };
				return;
			}

			var vertices = new List<OverlayVertex>();
			var indices = new List<ushort>();
			var commands = new List<DrawCommand>();
			var surfaceRect = new Rect(0f, 0f, root.SurfaceSize.X, root.SurfaceSize.Y);
			Emit(root, Matrix3x2.Identity, vertices, indices, commands, surfaceRect);
			var changed = new DrawListData
			{
				Vertices = vertices.ToArray(),
				Indices = indices.ToArray(),
				Commands = commands.ToArray(),
				TextureSlots = [TextureSource.White],
				CoordinateSize = root.SurfaceSize,
				Revision = ++_revision,
				TextureRevision = 1,
				Damage = surfaceRect,
			};
			var unchanged = new DrawListData
			{
				Vertices = changed.Vertices,
				Indices = changed.Indices,
				Commands = changed.Commands,
				TextureSlots = changed.TextureSlots,
				CoordinateSize = changed.CoordinateSize,
				Revision = changed.Revision,
				TextureRevision = changed.TextureRevision,
			};
			_lists[surface.Value] = new Built(changed, unchanged, true);
		}

		public DrawListData LastDraw(DrawListId surface) =>
			_lists.TryGetValue(surface.Value, out var built) ? built.Fresh ? built.Changed : built.Unchanged : DrawListData.Empty;

		private readonly record struct Built(DrawListData Changed, DrawListData Unchanged, bool Fresh);

		private void Changes(Visual v)
		{
			if (!_seen.TryGetValue(v, out var seen) || seen != v.Version)
			{
				_seen[v] = v.Version;
				ChangedVisuals++;
			}
			foreach (var child in v.Children)
				Changes(child);
		}

		private static void Emit(Visual v, Matrix3x2 parent, List<OverlayVertex> vertices, List<ushort> indices, List<DrawCommand> commands, Rect clip)
		{
			var matrix = v.Transform * parent;
			if (v is SpriteVisual { Content: RectContent { Fill: CompositionColorBrush fill } rect })
			{
				var first = vertices.Count;
				var color = fill.Color.ToPremultiplied(v.Opacity);
				foreach (var corner in (ReadOnlySpan<Vector2>)[Vector2.Zero, new(rect.Size.X, 0f), rect.Size, new(0f, rect.Size.Y)])
					vertices.Add(new OverlayVertex(Vector2.Transform(corner, matrix), Vector2.Zero, color, Vector4.Zero));
				commands.Add(new DrawCommand(clip, 0, indices.Count, 6, first, (uint)UIPrimKind.Fill));
				indices.AddRange([0, 1, 2, 0, 2, 3]);
			}
			foreach (var child in v.Children)
				Emit(child, matrix, vertices, indices, commands, clip);
		}
	}
}
