namespace NotasyonOtomasyonu.App;

/// <summary>
/// Büyük pencereler küçük dizüstü ekranında ya da yüksek ölçekte (%125–150) ekrandan taşmasın:
/// açılırken çalışma alanının (görev çubuğu hariç) %94'üne sığdırılır ve ortalanır; en küçük boyut da
/// ekranı aşmaz.
/// </summary>
internal static class WindowFit
{
    public static void FitToScreen(this Form form)
    {
        form.Load += (_, _) =>
        {
            var wa = (form.Owner is { } owner ? Screen.FromControl(owner) : Screen.FromPoint(Cursor.Position)).WorkingArea;
            int maxW = (int)(wa.Width * 0.94), maxH = (int)(wa.Height * 0.94);
            form.MinimumSize = new Size(Math.Min(form.MinimumSize.Width, maxW), Math.Min(form.MinimumSize.Height, maxH));
            if (form.Width <= maxW && form.Height <= maxH && wa.Contains(form.Bounds)) return;
            form.Size = new Size(Math.Min(form.Width, maxW), Math.Min(form.Height, maxH));
            form.Location = new Point(wa.Left + (wa.Width - form.Width) / 2, wa.Top + (wa.Height - form.Height) / 2);
        };
    }
}
