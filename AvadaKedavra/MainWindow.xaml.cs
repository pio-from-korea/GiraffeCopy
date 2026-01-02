using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms; // WinForms용 별칭

namespace AvadaKedavra
{
    public partial class MainWindow : Window
    {
        // --- Win32 API 선언 ---
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        private const int WM_CLIPBOARDUPDATE = 0x031D;
        // -----------------------

        private Forms.NotifyIcon? _notifyIcon; // Nullable 선언으로 경고 해결
        private string? _lastSignature = null;
        private IntPtr _windowHandle;

        public MainWindow()
        {
            InitializeComponent();
            SetupTrayIcon();
        }
        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            // URL을 기본 브라우저로 엽니다.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            _windowHandle = new WindowInteropHelper(this).Handle;
            HwndSource? src = HwndSource.FromHwnd(_windowHandle);
            src?.AddHook(WndProc);

            AddClipboardFormatListener(_windowHandle);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE)
            {
                //ProcessClipboard();
                Task.Delay(200).ContinueWith(_ =>
                {
                    this.Dispatcher.Invoke(() => ProcessClipboard());
                });
            }
            return IntPtr.Zero;
        }

        private void ProcessClipboard2()
        {
            try
            {
                // 명확하게 System.Windows(WPF)의 Clipboard 사용
                if (!System.Windows.Clipboard.ContainsData(System.Windows.DataFormats.Html)) return;

                System.Windows.IDataObject? dobj = System.Windows.Clipboard.GetDataObject();
                if (dobj == null) return;

                string? html = dobj.GetData(System.Windows.DataFormats.Html) as string;

                string currentSig = BuildSignature(dobj, html);
                if (currentSig == _lastSignature) return;

                if (html != null && html.Contains("photos.fife"))
                {
                    BitmapSource bitmap = System.Windows.Clipboard.GetImage();
                    if (bitmap != null)
                    {
                        System.Windows.DataObject newObj = new System.Windows.DataObject();
                        newObj.SetImage(bitmap);

                        using (MemoryStream ms = new MemoryStream())
                        {
                            PngBitmapEncoder encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            encoder.Save(ms);
                            newObj.SetData("PNG", new MemoryStream(ms.ToArray()));
                        }

                        _lastSignature = BuildSignature(newObj, null);
                        System.Windows.Clipboard.SetDataObject(newObj, true);

                        UpdateStatus("Google Photos 이미지 정리 완료");
                    }
                }
                else
                {
                    _lastSignature = currentSig;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Clipboard Error: " + ex.Message);
            }
        }

        private void ProcessClipboard()
        {
            try
            {
                // 1. 데이터 객체 획득 (WPF 방식)
                System.Windows.IDataObject? dobj = System.Windows.Clipboard.GetDataObject();
                if (dobj == null) return;

                // 2. HTML 여부 확인
                string? html = dobj.GetData(System.Windows.DataFormats.Html) as string;

                string currentSig = BuildSignature(dobj, html);
                if (currentSig == _lastSignature) return;

                // 구글 포토 패턴 감지
                bool isTarget = html != null && (html.Contains("photos.fife") || html.Contains("googleusercontent"));

                if (isTarget)
                {
                    BitmapSource? bitmap = null;

                    // [파이어폭스 대응 핵심] WinForms의 Clipboard API를 사용하여 '진짜' 비트맵 데이터 추출
                    // WPF의 GetImage()가 실패할 때를 대비한 2단계 전략입니다.
                    if (Forms.Clipboard.ContainsImage())
                    {
                        using (var drawingImage = Forms.Clipboard.GetImage())
                        {
                            if (drawingImage != null)
                            {
                                // System.Drawing.Image를 WPF용 BitmapSource로 변환
                                var bitmapContent = new System.Drawing.Bitmap(drawingImage);
                                var hBitmap = bitmapContent.GetHbitmap();
                                try
                                {
                                    bitmap = Imaging.CreateBitmapSourceFromHBitmap(
                                        hBitmap, IntPtr.Zero, Int32Rect.Empty,
                                        BitmapSizeOptions.FromEmptyOptions());
                                }
                                finally
                                {
                                    DeleteObject(hBitmap); // 메모리 누수 방지
                                }
                            }
                        }
                    }

                    // 검증 및 클립보드 교체
                    if (bitmap != null && bitmap.Width > 10)
                    {
                        System.Windows.DataObject newObj = new System.Windows.DataObject();
                        newObj.SetImage(bitmap);

                        using (MemoryStream ms = new MemoryStream())
                        {
                            PngBitmapEncoder encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            encoder.Save(ms);
                            newObj.SetData("PNG", new MemoryStream(ms.ToArray()));
                        }

                        _lastSignature = BuildSignature(newObj, null);
                        System.Windows.Clipboard.SetDataObject(newObj, true);

                        UpdateStatus("클립보드 이미지 최적화 완료");
                    }
                }
                else
                {
                    _lastSignature = currentSig;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error: " + ex.Message);
            }
        }

        // Win32 메모리 해제를 위한 선언 (클래스 내부에 추가)
        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        private string BuildSignature(System.Windows.IDataObject dobj, string? html)
        {
            string formats = string.Join("|", dobj.GetFormats());
            return $"{formats}_{html?.Length ?? 0}";
        }

        private void UpdateStatus(string message)
        {
            // UI Thread 안전하게 접근
            this.Dispatcher.Invoke(() => {
                StatusText.Text = $"[{DateTime.Now:HH:mm:ss}] {message}";
                if (_notifyIcon != null)
                {
                    _notifyIcon.BalloonTipText = message;
                    _notifyIcon.ShowBalloonTip(1000);
                }
            });
        }

        private void SetupTrayIcon()
        {
            _notifyIcon = new Forms.NotifyIcon();

            try
            {
                // 애플리케이션 실행 파일에 내장된 기본 아이콘을 추출하여 트레이에 사용
                // 이 방식은 별도의 파일 경로를 관리할 필요가 없어 가장 안전합니다.
                using (var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/dddddddd.ico"))?.Stream)
                {
                    if (stream != null)
                    {
                        _notifyIcon.Icon = new System.Drawing.Icon(stream);
                    }
                }
            }
            catch
            {
                // 만약 리소스 로드에 실패할 경우 시스템 기본 아이콘 사용
                _notifyIcon.Icon = SystemIcons.Application;
            }

            _notifyIcon.Visible = true;

            // ... 나머지 메뉴 설정 코드 ...
            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            menu.Items.Add("열기", null, (s, e) => {
                this.Dispatcher.Invoke(() => {
                    this.Show();
                    this.WindowState = WindowState.Normal;
                });
            });
            menu.Items.Add("종료", null, (s, e) => ExitApp());
            _notifyIcon.ContextMenuStrip = menu;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        private void ExitApp()
        {
            RemoveClipboardFormatListener(_windowHandle);
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            System.Windows.Application.Current.Shutdown();
        }
    }
}