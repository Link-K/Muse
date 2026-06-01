using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace Muse.Controls
{
	public class SafeGridSplitter : ContentControl
	{
		public static readonly StyledProperty<GridResizeDirection> ResizeDirectionProperty =
			AvaloniaProperty.Register<SafeGridSplitter, GridResizeDirection>(nameof(ResizeDirection), GridResizeDirection.Columns);

		public static readonly StyledProperty<GridResizeBehavior> ResizeBehaviorProperty =
			AvaloniaProperty.Register<SafeGridSplitter, GridResizeBehavior>(nameof(ResizeBehavior), GridResizeBehavior.PreviousAndNext);

		public GridResizeDirection ResizeDirection
		{
			get => GetValue(ResizeDirectionProperty);
			set => SetValue(ResizeDirectionProperty, value);
		}

		public GridResizeBehavior ResizeBehavior
		{
			get => GetValue(ResizeBehaviorProperty);
			set => SetValue(ResizeBehaviorProperty, value);
		}

		public SafeGridSplitter()
		{
			AttachedToVisualTree += OnAttachedToVisualTree;
		}

		private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
		{
			// Only attempt to create the real GridSplitter at runtime when attached to visual tree
			try
			{
				var gs = new Avalonia.Controls.GridSplitter();

				// copy basic layout properties
				gs.Width = this.Width;
				gs.Height = this.Height;
				gs.Background = this.Background;

				// apply our styled properties
				gs.ResizeDirection = this.ResizeDirection;
				gs.ResizeBehavior = this.ResizeBehavior;

				// set as content so it participates in layout
				this.Content = gs;
			}
			catch
			{
				// If platform services are not available (headless tests), leave Content null
			}
		}
	}
}
