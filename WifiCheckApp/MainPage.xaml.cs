using Microsoft.Maui.Storage;
using System.Collections.ObjectModel;
using WifiCheckApp.Services;

namespace WifiCheckApp
{
    public partial class MainPage : ContentPage
    {
        private readonly string _targetWifiName = "THE";
        private readonly string _targetGateway = "192.168.1.1";
        private readonly IConnectivity _connectivity;
        private readonly WifiService _wifiService;
        private readonly EmailService _emailService;
        private readonly LocationService _locationService;
        private readonly AttendanceSettings _attendanceSettings;
        private CancellationTokenSource _statusCheckCts;
        private ObservableCollection<WifiInfo> _availableNetworks = new ObservableCollection<WifiInfo>();
        private bool _isUpdatingStatus = false;
        private DateTime _lastStatusUpdateTime = DateTime.MinValue;
        private const int STATUS_UPDATE_INTERVAL_MS = 5000; // 5 seconds between status checks

        // Lưu kết quả kiểm tra kết nối để tối ưu hiệu suất
        private bool _lastWifiConnectionStatus = false;
        private bool _lastLocationStatus = false;
        private DateTime _lastConnectionCheckTime = DateTime.MinValue;

        // Flag để theo dõi khi đang thay đổi phương thức
        private bool _isChangingMethod = false;

        // Semaphore để đồng bộ hóa các thao tác kiểm tra trạng thái
        private SemaphoreSlim _statusCheckSemaphore = new SemaphoreSlim(1, 1);

        public MainPage(IConnectivity connectivity, WifiService wifiService, EmailService emailService,
                        LocationService locationService, AttendanceSettings attendanceSettings)
        {
            InitializeComponent();
            _connectivity = connectivity;
            _wifiService = wifiService;
            _emailService = emailService;
            _locationService = locationService;
            _attendanceSettings = attendanceSettings;

            RefreshPanel.IsVisible = true;
            ButtonsPanel.IsVisible = false; // Đảm bảo ẩn khi khởi động

            // Initialize attendance method picker
            InitializeAttendanceMethodPicker();

            // Show current time
            SetupTimeDisplay();

            // Load saved company location in background
            Task.Run(_locationService.SaveCompanyLocation);

            // Load saved email
            LoadSavedEmailAsync();
        }

        private void SetupTimeDisplay()
        {
            // Update time label every second
            Device.StartTimer(TimeSpan.FromSeconds(1), () =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    TimeLabel.Text = DateTime.Now.ToString("HH:mm:ss");
                });
                return true;
            });
        }

        private void InitializeAttendanceMethodPicker()
        {
            AttendanceMethodPicker.Items.Add("Wi-Fi");
            AttendanceMethodPicker.Items.Add("Vị trí");

            var currentMethod = _attendanceSettings.GetAttendanceMethod();
            AttendanceMethodPicker.SelectedIndex = (int)currentMethod;

            // Không gán sự kiện ngay lập tức để tránh việc trigger event khi thiết lập SelectedIndex
            // Sẽ gán sự kiện sau khi đã load xong form

            // Update UI based on current method - không cần check status ngay
            UpdateMethodDependentUIElements(currentMethod);
        }

        protected override async void OnAppearing()
        {
            try
            {
                base.OnAppearing();

                // Gán sự kiện sau khi form đã load xong
                AttendanceMethodPicker.SelectedIndexChanged += AttendanceMethodPicker_SelectedIndexChanged;

                // Request location permission
                await RequestLocationPermissionAsync();

                // Perform immediate status check before starting periodic checks
                await PerformStatusCheckAsync(forceUpdate: true);

                // Start periodic status checking
                StartPeriodicStatusChecks();
            }
            catch (Exception e)
            {
                await DisplayAlert("Lỗi", $"Không thể khởi tạo ứng dụng: {e.Message}", "OK");
            }
        }

        private async void AttendanceMethodPicker_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                // Sử dụng semaphore để tránh xung đột với các thao tác khác
                await _statusCheckSemaphore.WaitAsync();

                // Đặt cờ đang thay đổi phương thức
                _isChangingMethod = true;

                // Ẩn các panel khi đang thay đổi phương thức
                ButtonsPanel.IsVisible = false;
                EmailFrame.IsVisible = false;

                var selectedMethod = (AttendanceSettings.AttendanceMethod)AttendanceMethodPicker.SelectedIndex;

                // Nếu phương thức không thay đổi, không cần thực hiện các bước tiếp theo
                if (_attendanceSettings.GetAttendanceMethod() == selectedMethod)
                {
                    _isChangingMethod = false;
                    _statusCheckSemaphore.Release();
                    return;
                }

                _attendanceSettings.SetAttendanceMethod(selectedMethod);

                // Cập nhật UI ngay lập tức
                UpdateMethodDependentUIElements(selectedMethod);

                // Đánh dấu cần kiểm tra lại trạng thái
                _lastStatusUpdateTime = DateTime.MinValue;

                // Tạm dừng periodic check trong thời gian thay đổi phương thức
                StopPeriodicStatusChecks();

                // Check status ngay lập tức và force update UI
                await PerformStatusCheckInternalAsync(forceUpdate: true);

                // Khởi động lại periodic check sau khi đã hoàn tất thay đổi
                StartPeriodicStatusChecks();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Method change error: {ex.Message}");
            }
            finally
            {
                // Đảm bảo luôn reset cờ dù có lỗi
                _isChangingMethod = false;
                _statusCheckSemaphore.Release();
            }
        }

        private void UpdateMethodDependentUIElements(AttendanceSettings.AttendanceMethod method)
        {
            bool showWifiElements = (method == AttendanceSettings.AttendanceMethod.Wifi);
            bool showLocationElements = (method == AttendanceSettings.AttendanceMethod.Location);

            // Cập nhật UI ngay lập tức
            WifiStatusLabel.IsVisible = showWifiElements;
            if (LocationStatusLabel != null)
            {
                LocationStatusLabel.IsVisible = showLocationElements;
            }

            // Reset label text để người dùng biết đang kiểm tra trạng thái
            if (showWifiElements)
            {
                WifiStatusLabel.Text = "Đang kiểm tra kết nối wifi...";
                WifiStatusLabel.TextColor = Colors.Orange;
            }

            if (showLocationElements && LocationStatusLabel != null)
            {
                LocationStatusLabel.Text = "Đang kiểm tra vị trí...";
                LocationStatusLabel.TextColor = Colors.Orange;
            }
        }

        private async Task<bool> PerformStatusCheckAsync(bool forceUpdate = false)
        {
            // Sử dụng semaphore để đảm bảo chỉ có một thao tác kiểm tra trạng thái được thực hiện tại một thời điểm
            await _statusCheckSemaphore.WaitAsync();
            try
            {
                return await PerformStatusCheckInternalAsync(forceUpdate);
            }
            finally
            {
                _statusCheckSemaphore.Release();
            }
        }

        private async Task<bool> PerformStatusCheckInternalAsync(bool forceUpdate = false)
        {
            // Don't check if another check is already in progress
            if (_isUpdatingStatus && !forceUpdate)
                return false;

            // Don't check too frequently unless forced
            if (!forceUpdate && (DateTime.Now - _lastStatusUpdateTime).TotalMilliseconds < STATUS_UPDATE_INTERVAL_MS)
                return false;

            try
            {
                _isUpdatingStatus = true;
                _lastStatusUpdateTime = DateTime.Now;

                var attendanceMethod = _attendanceSettings.GetAttendanceMethod();
                bool isWifiConnected = false;
                bool isLocationValid = false;

                // Kiểm tra trạng thái dựa vào phương thức hiện tại
                if (attendanceMethod == AttendanceSettings.AttendanceMethod.Wifi)
                {
                    isWifiConnected = await _wifiService.IsConnectedToTargetWifi(_targetWifiName, _targetGateway);
                    _lastWifiConnectionStatus = isWifiConnected;
                    _lastConnectionCheckTime = DateTime.Now;
                }
                else if (attendanceMethod == AttendanceSettings.AttendanceMethod.Location)
                {
                    isLocationValid = await _locationService.IsWithinCompanyRange();
                    _lastLocationStatus = isLocationValid;
                    _lastConnectionCheckTime = DateTime.Now;
                }

                // Determine overall status
                bool isStatusValid = DetermineOverallStatus(attendanceMethod, isWifiConnected, isLocationValid);

                // Update UI based on status
                await UpdateStatusUI(isStatusValid, isWifiConnected, isLocationValid);

                return isStatusValid;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Status check error: {ex.Message}");
                return false;
            }
            finally
            {
                _isUpdatingStatus = false;
            }
        }

        private bool DetermineOverallStatus(AttendanceSettings.AttendanceMethod method, bool wifiStatus, bool locationStatus)
        {
            return method switch
            {
                AttendanceSettings.AttendanceMethod.Wifi => wifiStatus,
                AttendanceSettings.AttendanceMethod.Location => locationStatus,
                _ => false
            };
        }

        private async Task UpdateStatusUI(bool isStatusValid, bool isWifiConnected, bool isLocationValid)
        {
            // Nếu đang trong quá trình thay đổi phương thức, không cập nhật UI
            if (_isChangingMethod)
            {
                return;
            }

            // Đảm bảo cập nhật UI trên Main Thread
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                UpdateStatusUIInternal(isStatusValid, isWifiConnected, isLocationValid);
            });
        }

        private async void UpdateStatusUIInternal(bool isStatusValid, bool isWifiConnected, bool isLocationValid)
        {
            string savedEmail = await _emailService.GetSavedEmail();
            var attendanceMethod = _attendanceSettings.GetAttendanceMethod();

            // Update WiFi status if needed
            if (attendanceMethod == AttendanceSettings.AttendanceMethod.Wifi)
            {
                WifiStatusLabel.Text = isWifiConnected ? "Đã kết nối đến wifi công ty" : "Không kết nối đến wifi công ty";
                WifiStatusLabel.TextColor = isWifiConnected ? Colors.Green : Colors.Red;
            }

            // Update location status if needed
            if ((attendanceMethod == AttendanceSettings.AttendanceMethod.Location) && LocationStatusLabel != null)
            {
                Task.Run(async () =>
                {
                    if (isLocationValid)
                    {
                        double distance = await _locationService.GetDistanceFromCompany();
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            LocationStatusLabel.Text = distance >= 0 ?
                                $"Trong phạm vi công ty ({distance:F1}m)" :
                                "Trong phạm vi công ty (20m)";
                            LocationStatusLabel.TextColor = Colors.Green;
                        });
                    }
                    else
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            LocationStatusLabel.Text = "Ngoài phạm vi công ty";
                            LocationStatusLabel.TextColor = Colors.Red;
                        });
                    }
                });
            }

            // Determine if at least one method is valid for showing email entry
            bool atLeastOneMethodValid = isStatusValid;

            // Show email input if at least one method is valid AND we don't have a saved email
            EmailFrame.IsVisible = atLeastOneMethodValid && string.IsNullOrEmpty(savedEmail);

            // Email label display
            EmailLabel.IsVisible = !string.IsNullOrEmpty(savedEmail);
            EmailLabel.Text = savedEmail ?? string.Empty;

            // Show buttons only if overall status is valid AND we have a saved email
            ButtonsPanel.IsVisible = isStatusValid && !string.IsNullOrEmpty(savedEmail);

            // Always make sure RefreshPanel is visible when needed
            RefreshPanel.IsVisible = !isStatusValid;
        }

        private async void LoadSavedEmailAsync()
        {
            try
            {
                string savedEmail = await _emailService.GetSavedEmail();
                if (!string.IsNullOrEmpty(savedEmail))
                {
                    EmailEntry.Text = savedEmail;
                    EmailLabel.Text = savedEmail;
                    EmailLabel.IsVisible = true;
                }
            }
            catch (Exception e)
            {
                await DisplayAlert("Lỗi", $"Không thể tải email đã lưu: {e.Message}", "OK");
            }
        }

        private async void OnSaveEmailClicked(object sender, EventArgs e)
        {
            string email = EmailEntry.Text?.Trim();

            if (string.IsNullOrEmpty(email) || !IsValidEmail(email))
            {
                await DisplayAlert("Lỗi", "Vui lòng nhập email hợp lệ", "OK");
                return;
            }

            await _emailService.SaveEmail(email);

            EmailLabel.Text = email;
            EmailLabel.IsVisible = true;
            EmailFrame.IsVisible = false;

            // Check status again to potentially show buttons
            await PerformStatusCheckAsync(forceUpdate: true);
        }

        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        private async void OnCheckInClicked(object sender, EventArgs e)
        {
            // Hiện thông báo đang kiểm tra
            ButtonsPanel.IsEnabled = false;

            bool isValidConnection = await ValidateConnectionBasedOnMethod();

            // Đảm bảo kích hoạt lại nút
            ButtonsPanel.IsEnabled = true;

            if (!isValidConnection)
            {
                await DisplayAlert("Lỗi", "Không thể chấm công. Vui lòng kiểm tra kết nối Wi-Fi hoặc vị trí.", "OK");
                return;
            }

            string timestamp = DateTime.Now.ToString("HH:mm:ss dd/MM/yyyy");
            var methodText = GetAttendanceMethodText();
            await DisplayAlert("Chấm công", $"Thời gian chấm công: {timestamp}\nPhương thức: {methodText}", "OK");
        }

        private async void OnCheckOutClicked(object sender, EventArgs e)
        {
            // Hiện thông báo đang kiểm tra
            ButtonsPanel.IsEnabled = false;

            bool isValidConnection = await ValidateConnectionBasedOnMethod();

            // Đảm bảo kích hoạt lại nút
            ButtonsPanel.IsEnabled = true;

            if (!isValidConnection)
            {
                await DisplayAlert("Lỗi", "Không thể ra về. Vui lòng kiểm tra kết nối Wi-Fi hoặc vị trí.", "OK");
                return;
            }

            string timestamp = DateTime.Now.ToString("HH:mm:ss dd/MM/yyyy");
            var methodText = GetAttendanceMethodText();
            await DisplayAlert("Ra về", $"Thời gian ra về: {timestamp}\nPhương thức: {methodText}", "OK");
        }

        private async Task<bool> ValidateConnectionBasedOnMethod()
        {
            var attendanceMethod = _attendanceSettings.GetAttendanceMethod();

            // Sử dụng kết quả đã lưu nếu kiểm tra gần đây (trong vòng 2 giây)
            if ((DateTime.Now - _lastConnectionCheckTime).TotalSeconds <= 2)
            {
                return attendanceMethod switch
                {
                    AttendanceSettings.AttendanceMethod.Wifi => _lastWifiConnectionStatus,
                    AttendanceSettings.AttendanceMethod.Location => _lastLocationStatus,
                    _ => false
                };
            }

            // Nếu chưa kiểm tra gần đây, kiểm tra lại ngay lập tức
            try
            {
                // Kiểm tra kết nối theo phương thức được chọn
                return await PerformStatusCheckAsync(forceUpdate: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Connection validation error: {ex.Message}");
                return false;
            }
        }

        private string GetAttendanceMethodText()
        {
            return _attendanceSettings.GetAttendanceMethod() switch
            {
                AttendanceSettings.AttendanceMethod.Wifi => "Wi-Fi",
                AttendanceSettings.AttendanceMethod.Location => "Vị trí",
                _ => "Không xác định"
            };
        }

        private async void OnRefreshClicked(object sender, EventArgs e)
        {
            // Vô hiệu hóa nút Refresh trong khi đang kiểm tra
            RefreshButton.IsEnabled = false;

            await PerformStatusCheckAsync(forceUpdate: true);

            // Kích hoạt lại nút sau khi đã kiểm tra xong
            RefreshButton.IsEnabled = true;
        }

        private async Task RequestLocationPermissionAsync()
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();

            if (status != PermissionStatus.Granted)
            {
                await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            // Stop status checking when page is not visible
            StopPeriodicStatusChecks();
        }

        private void StartPeriodicStatusChecks()
        {
            StopPeriodicStatusChecks(); // Stop any existing timer

            _statusCheckCts = new CancellationTokenSource();

            // Start a periodic task instead of Timer - sử dụng cách tiếp cận non-blocking
            Task.Run(async () =>
            {
                while (!_statusCheckCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        // Chờ một khoảng thời gian trước khi kiểm tra
                        await Task.Delay(STATUS_UPDATE_INTERVAL_MS, _statusCheckCts.Token).ConfigureAwait(false);

                        // Sử dụng ConfigureAwait(false) để tránh deadlock
                        await PerformStatusCheckAsync().ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        break; // Exit loop if task was canceled
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Status check error: {ex.Message}");
                        // Ngủ ngắn rồi thử lại để tránh vòng lặp liên tục
                        await Task.Delay(1000, _statusCheckCts.Token).ConfigureAwait(false);
                    }
                }
            }, _statusCheckCts.Token);
        }

        private void StopPeriodicStatusChecks()
        {
            if (_statusCheckCts != null && !_statusCheckCts.IsCancellationRequested)
            {
                _statusCheckCts.Cancel();
                _statusCheckCts.Dispose();
                _statusCheckCts = null;
            }
        }
    }
}