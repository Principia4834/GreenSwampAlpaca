namespace GreenSwamp.Alpaca.Server.Services
{
    public class FloatingWindowManager
    {
        public event Action? OnStateChanged;

        public bool IsVisible { get; private set; }
        public double X { get; private set; } = 100;
        public double Y { get; private set; } = 100;
        public double Width { get; private set; } = 400;
        public double Height { get; private set; } = 300;

        public string Title { get; private set; } = "Persistent Tool Window";
        public string ContentText { get; private set; } = string.Empty;

        public void Open()
        {
            IsVisible = true;
            Notify();
        }

        public void Open(string title, string contentText)
        {
            Title = title;
            ContentText = contentText;
            IsVisible = true;
            Notify();
        }

        public void Close()
        {
            IsVisible = false;
            Notify();
        }

        public void UpdatePosition(double x, double y)
        {
            X = x;
            Y = y;
            Notify();
        }

        public void UpdateSize(double width, double height)
        {
            Width = Math.Max(200, width);
            Height = Math.Max(150, height);
            Notify();
        }

        private void Notify() => OnStateChanged?.Invoke();
    }
}