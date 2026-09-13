# Logic dự án: YouTube Channel Downloader

Tài liệu này mô tả toàn bộ logic nghiệp vụ và kiến trúc mã nguồn của ứng dụng
desktop WPF `MultiDownloadVideoYoutube` (tên assembly xuất bản:
`YouTubeChannelDownloader`), được tổng hợp từ việc đọc trực tiếp mã nguồn.

## 1. Mục đích ứng dụng

Ứng dụng Windows (C# 12, .NET 8, WPF, MVVM) cho phép:

1. Nhập URL một kênh YouTube.
2. Dùng `yt-dlp` để liệt kê (không tải) video/Shorts/livestream đã phát lại
   của kênh đó.
3. Lọc danh sách theo thời lượng và/hoặc số video gần nhất.
4. Chọn video muốn tải, rồi tải **tuần tự từng video một** bằng `yt-dlp` +
   `ffmpeg`/`ffprobe`, có tiến trình, tốc độ, ETA theo thời gian thực.
5. Quản lý nhiều "chủ đề" (tab) độc lập cùng lúc, mỗi tab có cấu hình riêng.
6. Lưu/khôi phục cấu hình (không lưu cookie, token, danh sách video hay log).

Ứng dụng **không** bẻ khóa DRM, không vượt giới hạn thành viên/riêng tư — nếu
người dùng bật cookie trình duyệt, cookie chỉ được truyền thẳng cho tham số
`yt-dlp --cookies-from-browser`, không bao giờ được ghi ra file hay log.

## 2. Kiến trúc tổng thể

Mẫu kiến trúc: **MVVM** với Dependency Injection (`Microsoft.Extensions.
DependencyInjection`) và `CommunityToolkit.Mvvm` cho `ObservableObject`/
`RelayCommand`.

```
App.xaml.cs            → khởi tạo DI container, tạo MainWindow, gọi InitializeAsync
MainWindow.xaml(.cs)   → Shell WPF, chỉ xử lý sự kiện đóng cửa sổ
ViewModels/            → MainViewModel, DownloadTabViewModel, DownloadItemViewModel
Services/              → toàn bộ logic nghiệp vụ (I/O, gọi tiến trình ngoài, HTTP...)
Services/Interfaces/   → hợp đồng DI cho từng service
Helpers/               → hàm tiện ích thuần (URL, tên file, parser, filter, tiến trình)
Models/                → DTO/POCO dùng xuyên suốt tầng ViewModel ↔ Service
Enums/                 → các kiểu liệt kê trạng thái/loại nội dung
Tools/                 → chứa 3 file thực thi bên ngoài: yt-dlp.exe, ffmpeg.exe, ffprobe.exe
```

Toàn bộ service được đăng ký là **singleton** trong `App.xaml.cs`:

| Interface | Triển khai | Vai trò |
|---|---|---|
| `IAppLogger` | `FileAppLogger` | Ghi log ra file + phát sự kiện cho UI |
| `ISettingsService` | `SettingsService` | Đọc/ghi `settings.json` |
| `IDependencyService` | `DependencyService` | Kiểm tra yt-dlp/ffmpeg/ffprobe |
| `IArchiveService` | `ArchiveService` | Đọc file `archive.txt` của yt-dlp |
| `IFileDialogService` | `FileDialogService` | Hộp thoại chọn thư mục, clipboard, MessageBox |
| `IVideoDurationCache` | `FileVideoDurationCache` | Cache thời lượng video ra đĩa |
| `IVideoMetadataLogger` | `FileVideoMetadataLogger` | Ghi snapshot JSON sau mỗi lần phân tích |
| `HttpClient` | (mặc định) | Dùng cho YouTube Data API |
| `IYouTubeDataApiDurationResolver` | `YouTubeDataApiDurationResolver` | Lấy thời lượng qua YouTube Data API v3 |
| `IChannelAnalyzer` | `YtDlpChannelAnalyzer` | Phân tích kênh bằng yt-dlp flat-playlist |
| `IVideoDownloadService` | `YtDlpDownloadService` | Tải từng video bằng yt-dlp |
| — | `MainViewModel`, `MainWindow` | Gốc UI |

### 2.1. Luồng khởi động (`App.xaml.cs`)

1. Đọc `appsettings.json` (tùy chọn) bằng `ConfigurationBuilder`.
2. Build `ServiceCollection` như bảng trên.
3. Đăng ký `DispatcherUnhandledException`: mọi exception UI chưa bắt được sẽ
   được log (`IAppLogger.Error`) và hiển thị `MessageBox` lỗi chung chung,
   sau đó đánh dấu `e.Handled = true` để ứng dụng không crash.
4. Resolve `MainWindow`, `Show()`, rồi `await window.ViewModel.
   InitializeAsync()` — nghĩa là cửa sổ hiện ra trước, việc khởi tạo (kiểm
   tra dependency, nạp settings) chạy bất đồng bộ sau đó.
5. `OnExit`: gỡ handler và `Dispose()` service provider (giải phóng
   `FileAppLogger`, `YtDlpDownloadService`...).

### 2.2. Đóng ứng dụng (`MainWindow.xaml.cs`)

Sự kiện `Closing` bị chặn thủ công (`e.Cancel = true`) để đảm bảo dọn dẹp:

1. Nếu `ViewModel.IsBusy` (có tab đang phân tích/tải) → hỏi xác nhận
   `YesNo`. Chọn "No" thì hủy đóng.
2. Nếu đồng ý: `await ViewModel.CancelActiveOperationAsync()` (hủy mọi
   tiến trình yt-dlp đang chạy ở tất cả tab), rồi `await ViewModel.
   SaveSettingsAsync()` (tự động lưu cấu hình, không hiện confirmation).
3. Đặt cờ `_allowClose = true` rồi gọi `Close()` thật sự.
4. Có cờ `_closingInProgress` để tránh chạy song song nếu người dùng bấm
   đóng nhiều lần trong lúc đang dọn dẹp.

## 3. Mô hình dữ liệu (Models & Enums)

- `DownloadItem`: một video (id, tiêu đề, url, kênh, loại nội dung, thời
  lượng, ngày đăng, thumbnail, trạng thái tải, tiến trình, tốc độ, ETA, lỗi).
- `DownloadOptions`: tham số cho một lượt tải (thư mục ra, tên kênh, chất
  lượng, có tải thumbnail/phụ đề không, có bỏ qua video đã tải không, cookie
  trình duyệt).
- `DownloadResult`: kết quả một lượt tải (`Status`, `OutputFilePath`,
  `ErrorMessage`).
- `DownloadProgressMessage`: bản tin realtime từ tiến trình yt-dlp
  (`ProgressMessageType`, id video, message, phần trăm, tốc độ, eta,
  đường dẫn output).
- `ChannelAnalysisResult`: kết quả phân tích kênh (tên kênh + danh sách
  `DownloadItem`).
- `AppSettings` / `DownloadTabSettings`: cấu hình lưu trên đĩa (xem mục 7).
- `DependencyInfo` / `DependencyCheckResult`: kết quả kiểm tra 3 công cụ
  ngoài, `IsSuccess` = tất cả có sẵn.
- `DownloadQualityOption`, `DurationComparisonOption`, `DurationUnitOption`:
  cặp (nhãn hiển thị, giá trị enum) để bind ComboBox trong UI.
- `LogEntry`: một dòng log có timestamp, cấp độ, message, id video tùy chọn;
  `ToString()` định dạng `[yyyy-MM-dd HH:mm:ss] [Level] [videoId] message`.

Enum quan trọng:

- `ContentType`: `Video | Short | Stream`.
- `DownloadStatus`: `Pending, Analyzing, Ready, Downloading, Processing,
  Completed, AlreadyDownloaded, RequiresLogin, Private, Unavailable,
  Skipped, Failed, Cancelled`.
- `DownloadQuality`: `Best, P2160, P1440, P1080, P720, P480, AudioMp3`.
- `DurationFilterComparison`: `ShorterThan | LongerThan`.
- `DurationFilterUnit`: `Seconds | Minutes | Hours`.
- `ProgressMessageType`: `Start, Progress, Complete, Processing, Warning,
  Error, Debug`.
- `AppLogLevel`: `Info, Warning, Error, Debug`.

## 4. Tầng ViewModel — luồng nghiệp vụ chính

### 4.1. `MainViewModel` (điều phối nhiều tab)

- Giữ `ObservableCollection<DownloadTabViewModel> Tabs` và `SelectedTab`.
- `IsBusy` = `true` nếu **bất kỳ** tab nào đang bận (phân tích hoặc tải).
- `AddTabCommand`: tạo tab mới (`CreateTab`), copy cấu hình dùng chung từ
  tab đang chọn (`CopySharedStateFrom` — output dir, chất lượng, các cờ tải,
  bộ lọc...), thêm vào danh sách và chọn làm tab hiện tại. Chỉ khả dụng sau
  khi khởi tạo xong (`_initialized`).
- `CloseTabCommand`: chỉ cho đóng khi còn > 1 tab và tab đó không bận. Sau
  khi đóng, chọn tab lân cận (`Math.Clamp` theo vị trí đã xóa) làm tab hiện
  tại, gọi `Dispose()` trên tab bị đóng để gỡ sự kiện.
- `SaveSettingsCommand`: lưu toàn bộ tab, hiện `MessageBox` xác nhận (khác
  với lưu tự động khi đóng ứng dụng, không hiện thông báo).
- `InitializeAsync()`:
  1. Nạp `AppSettings` từ `ISettingsService`.
  2. Lấy `GetSavedTabs()` (nếu file cũ chưa có `Tabs[]`, tự tạo 1 tab từ các
     trường "legacy" phẳng để tương thích ngược).
  3. Khởi tạo tab đầu tiên với `InitializeAsync` (kiểm tra dependency thật
     sự); các tab còn lại chỉ `ApplySettings` rồi copy nhanh trạng thái
     dependency (`CopyRuntimeState`) từ tab đầu — tránh kiểm tra
     yt-dlp/ffmpeg lặp lại nhiều lần khi có nhiều tab.
  4. Khôi phục `SelectedTabIndex` đã lưu (kẹp trong khoảng hợp lệ).
- `SaveSettingsCoreAsync`: gom `CreateSettingsSnapshot()` của từng tab, đồng
  thời **đồng bộ ngược** các trường legacy (`LastOutputDirectory`,
  `LastQuality`...) bằng dữ liệu tab đang chọn, để file settings vẫn đọc
  được bởi phiên bản cũ hơn của ứng dụng. Lỗi ghi đĩa (`IOException`,
  `UnauthorizedAccessException`, `InvalidOperationException`) được log và
  báo lỗi UI nếu là thao tác thủ công.
- `CancelActiveOperationAsync()`: `Task.WhenAll` hủy tất cả tab đang bận.

### 4.2. `DownloadTabViewModel` (logic của một chủ đề/tab)

Đây là trung tâm nghiệp vụ, quản lý một kênh: URL, tùy chọn tải, bộ lọc,
danh sách video, tiến trình, log.

**Trạng thái quan trọng**

- `VideoItems`: toàn bộ video tìm thấy sau phân tích.
- `FilteredVideoItems`: tập con hiển thị sau khi áp bộ lọc thời lượng/số
  lượng gần nhất — đây là collection UI thật sự bind vào `DataGrid`.
- `IsBusy = IsAnalyzing || IsDownloading`.
- Nhiều thuộc tính thống kê tính runtime từ `FilteredVideoItems`:
  `TotalVideoCount`, `VisibleVideoCount`, `FilteredOutCount`,
  `SelectedVideoCount`, `PendingCount`, `CompletedCount`, `SkippedCount`,
  `FailedCount`.

**Khởi tạo tab (`InitializeAsync(DownloadTabSettings)`)**

1. `ApplySettings` nạp toàn bộ cấu hình đã lưu vào các thuộc tính binding.
2. Gọi `IDependencyService.CheckAsync` để kiểm tra 3 file exe (tồn tại +
   chạy được `--version`/`-version`).
3. Dựng `DependencySummary` (chuỗi nhiều dòng: tên + version hoặc "THIẾU").
4. Nếu thiếu bất kỳ dependency nào → `StatusText = "Có lỗi"` và hiện cảnh
   báo yêu cầu đặt file `.exe` vào thư mục `Tools`.
5. Bắt exception tổng quát để tránh crash lúc khởi tạo, log + báo lỗi.

**Phân tích kênh (`AnalyzeChannelAsync`, nút "Phân tích")**

Điều kiện bật nút: dependency sẵn sàng, không bận, có URL, có chọn ít nhất
1 loại nội dung (Video/Short/Stream).

1. Chuẩn hóa & validate URL qua `YouTubeUrlHelper.TryNormalizeChannelUrl`
   (mục 5.1) — nếu sai, cảnh báo và dừng.
2. `BeginOperation(isDownload: false)`: tạo `CancellationTokenSource` mới,
   set `IsAnalyzing = true`, xóa danh sách video cũ.
3. Gọi `IChannelAnalyzer.AnalyzeAsync(...)` (logic chi tiết ở mục 6.1) với
   các loại nội dung đã chọn, `RecentVideoLimit`, cờ `IsDurationFilterEnabled`
   (dùng để quyết định có cần fallback yt-dlp lấy duration hay không), cờ
   cookie trình duyệt.
4. Với mỗi video trả về: nếu `SkipPreviouslyDownloaded` bật và thư mục ra
   đã có, đọc `archive.txt` qua `IArchiveService` để đánh dấu video đã tải
   trước đó là `AlreadyDownloaded` và bỏ chọn.
5. Nếu bật bộ lọc thời lượng nhưng **toàn bộ** video không có metadata thời
   lượng → tự động tắt bộ lọc và cảnh báo người dùng cấu hình YouTube Data
   API key hoặc bật cookie.
6. `ApplyFilters()` để tính `FilteredVideoItems` (mục 4.2.3), cập nhật
   `StatusText` theo có tìm thấy video hay không.
7. Bắt riêng `OperationCanceledException` (đã hủy), `ChannelAnalysisException`
   (lỗi nghiệp vụ có thông điệp thân thiện) và exception khác (lỗi bất
   ngờ, chỉ log + thông báo chung).
8. `finally`: tắt `IsAnalyzing`, gọi `CompleteOperation()` để giải phóng
   CTS và báo hiệu `_operationStopped` (dùng khi đóng ứng dụng cần đợi).

**Tải xuống (`StartDownloadAsync`, nút "Tải")**

Điều kiện bật nút: dependency sẵn sàng, không bận, có thư mục ra, có ít
nhất một video được chọn và `CanSelect` (trạng thái cho phép tải).

1. `TryPrepareOutputDirectoryAsync()`: `Path.GetFullPath`, tạo thư mục nếu
   chưa có, **thử ghi một file tạm** (`FileMode.CreateNew` +
   `FileOptions.DeleteOnClose`) để xác nhận có quyền ghi thật sự trước khi
   bắt đầu hàng đợi — tránh tải nửa chừng rồi mới phát hiện lỗi quyền.
2. Lấy danh sách video đã chọn từ `FilteredVideoItems` (chỉ những item hiển
   thị sau lọc mới được tải).
3. `BeginOperation(isDownload: true)`, set `IsDownloading = true`.
4. **Tải tuần tự** (vòng `foreach`, không song song) từng `item`:
   - `PrepareItemForDownload`: reset tiến trình, set trạng thái
     `Downloading`, cập nhật khung thông tin hiện tại (tiêu đề/tốc độ/eta).
   - Tạo `Progress<DownloadProgressMessage>` gọi `ApplyProgress` mỗi khi có
     bản tin realtime (ánh xạ `ProgressMessageType` → cập nhật `Status`,
     `ProgressPercent`, `Speed`, `Eta`, `OutputFilePath`, `ErrorMessage`).
   - Gọi `IVideoDownloadService.DownloadAsync(item.ToModel(), options,
     progress, cancellationToken)`.
   - `ApplyDownloadResult`: gán `Status/OutputFilePath/ErrorMessage` cuối
     cùng, log tương ứng (thành công/đã tải trước/lỗi).
   - `RaiseStatisticsChanged()` sau mỗi video để cập nhật số liệu UI.
5. Khi hoàn tất: `StatusText` = "Hoàn thành" hoặc "Hoàn thành — có lỗi" nếu
   `FailedCount > 0`.
6. Khi bị hủy giữa chừng: nếu video hiện tại đang `Downloading`/`Processing`
   thì chuyển thành `Cancelled` với ghi chú "file `.part` được giữ để tiếp
   tục lần sau" (vì yt-dlp chạy với `--continue`).
7. `finally`: tắt `IsDownloading`, reset khung thông tin hiện tại về mặc
   định, `CompleteOperation()`.

**Hủy thao tác (`CancelDownloadCommand`)**

- Chỉ khả dụng khi đang bận và chưa hủy (`IsCancelling == false`).
- Gọi `_activeOperationCts.Cancel()` — điều này lan truyền `CancellationToken`
  xuống `ProcessHelper.RunAsync`, nơi tiến trình con (yt-dlp) và **toàn bộ
  cây tiến trình con của nó** (bao gồm ffmpeg) bị kill (mục 5.3).

**Bộ lọc (`ApplyFilters`)**

1. `RecentVideoFilterHelper.TakeMostRecent` giữ lại tối đa `RecentVideoLimit`
   video mới nhất theo `UploadDate` (0 = không giới hạn).
2. Trong tập đó, lọc tiếp theo `DurationFilterHelper.Matches` (thời lượng +
   so sánh + đơn vị, chỉ áp dụng khi `IsDurationFilterEnabled`).
3. Video không còn nằm trong tập hiển thị (`matchingItems`) sẽ bị tự động
   **bỏ chọn** (`IsSelected = false`) để tránh vô tình đưa vào hàng đợi tải,
   nhưng chỉ khi có bộ lọc nào đang hoạt động (`IsDurationFilterEnabled ||
   RecentVideoLimit > 0`).
4. Đổ lại `FilteredVideoItems` và raise lại toàn bộ số liệu thống kê +
   trạng thái nút lệnh.
5. Mọi thay đổi thuộc tính bộ lọc (`IsDurationFilterEnabled`,
   `SelectedDurationComparison`, `DurationLimitValue`,
   `SelectedDurationUnit`, `RecentVideoLimit`) đều tự gọi lại
   `ApplyFilters()` ngay khi set — lọc là realtime, không cần nút áp dụng.

**Các lệnh phụ khác**

- `SelectAllCommand`/`UnselectAllCommand`: chọn/bỏ chọn hàng loạt trên
  `FilteredVideoItems` (chỉ những video `CanSelect`), dùng cờ
  `_bulkSelectionChange` để tránh raise lại thống kê hàng trăm lần trong
  vòng lặp (chỉ raise một lần ở cuối).
- `BrowseOutputDirectoryCommand`: mở `OpenFolderDialog` (WPF mới, không
  phải Win32 Forms) qua `IFileDialogService`.
- `PasteUrlCommand`: đọc clipboard dạng Unicode text; bắt riêng
  `ExternalException` (clipboard bị khóa bởi tiến trình khác) để không
  crash.
- `OpenOutputDirectoryCommand`: mở Explorer bằng `Process.Start` với
  `UseShellExecute = true`.
- `ClearLogCommand`: chỉ khả dụng khi có log.
- `ConfirmTitleCommand`: chốt `TitleInput` (đang gõ) thành `Title` chính
  thức — tách hai thuộc tính để không đổi tên tab ngay từng ký tự gõ.
- Đăng ký `_logger.EntryLogged += OnLogEntryLogged` trong constructor: mỗi
  dòng log mới được đẩy vào `Logs` (giới hạn 2000 dòng, xóa dòng cũ nhất).
  Dùng `SynchronizationContext` bắt tại thời điểm tạo ViewModel (UI thread)
  để đảm bảo cập nhật `ObservableCollection` luôn chạy đúng thread dù log
  được ghi từ luồng nền (tiến trình con).

**Tự đặt tên tab theo kênh**: sau khi phân tích thành công, nếu tiêu đề tab
vẫn là mặc định dạng "Chủ đề N" hoặc "Chủ đề", tab sẽ tự đổi tên thành tên
kênh vừa phân tích được.

### 4.3. `DownloadItemViewModel` (một dòng trong bảng video)

- Bọc `DownloadItem` (model bất biến phần metadata: id, tiêu đề, url,
  kênh, loại, thời lượng, ngày đăng, thumbnail) với các thuộc tính runtime
  quan sát được (`IsSelected`, `ProgressPercent`, `Speed`, `Eta`, `Status`,
  `OutputFilePath`, `ErrorMessage`).
- `CanSelect`: chỉ `true` khi `Status` thuộc `Pending, Ready, Failed,
  Cancelled, Skipped` — video `Private/RequiresLogin/Unavailable/
  Downloading/Completed/AlreadyDownloaded` không thể tick chọn. Khi
  `Status` đổi khiến `CanSelect` thành `false`, tự động bỏ chọn
  (`IsSelected = false`).
- `DurationDisplay`: định dạng `h:mm:ss` nếu ≥ 1 giờ, ngược lại `m:ss`.
- `StatusDisplay`/`ContentTypeDisplay`: ánh xạ enum sang nhãn tiếng Việt.
- `ToModel()`: chuyển ngược về `DownloadItem` (dùng khi gửi cho
  `IVideoDownloadService.DownloadAsync`).

## 5. Helpers (logic thuần, dễ test)

### 5.1. `YouTubeUrlHelper`

- `TryNormalizeChannelUrl`: chuẩn hóa & validate URL kênh.
  - Bắt buộc scheme `http`/`https`, host `youtube.com` hoặc
    `*.youtube.com`.
  - Nhận diện 2 kiểu đường dẫn kênh: `/@handle` (1 segment) hoặc
    `/channel|c|user/<id>` (2 segment).
  - Từ chối rõ ràng URL video/short/live (`/watch`, `/shorts`, `/live`) với
    thông báo riêng ("Đây là URL video, không phải URL kênh").
  - Cho phép có thêm 1 segment là tab hợp lệ (`videos|shorts|streams`) phía
    sau, nhưng loại bỏ nó — kết quả luôn là URL kênh gốc, sạch query/fragment.
  - Trả về `Uri` đã chuẩn hóa (luôn `https://www.youtube.com/...`).
- `CreateTabUri`: nối thêm `/videos`, `/shorts` hoặc `/streams` vào URI gốc
  đã chuẩn hóa, dùng để phân tích riêng từng tab nội dung.

### 5.2. `FileNameHelper`

- `SanitizeDirectoryName`: loại bỏ ký tự không hợp lệ trong tên file
  Windows và ký tự điều khiển (thay bằng `_`), cắt khoảng trắng/`.` ở đầu
  cuối, chặn tên dành riêng của Windows (`CON, PRN, COM1...` → thêm tiền tố
  `_`), giới hạn 80 ký tự.
- `GetChannelDirectory`: `OutputDirectory/<TênKênhĐãSanitize>`.
- `GetContentFolderName`: `Video → "Videos"`, `Short → "Shorts"`,
  `Stream → "Streams"`.
- `BuildOutputTemplate`: mẫu đường dẫn ra cho yt-dlp:
  `<channelDir>/<Videos|Shorts|Streams>/%(upload_date>%Y-%m-%d)s -
  %(title).140B.%(ext)s` (giới hạn tiêu đề 140 byte để tránh vượt giới hạn
  đường dẫn Windows).

### 5.3. `ProcessHelper`

- `CreateStartInfo`: cấu hình chuẩn để chạy tiến trình ẩn, redirect
  stdout/stderr dạng UTF-8, không dùng shell.
- `RunAsync`: chạy tiến trình, đọc stdout/stderr **song song** (2 task bơm
  từng dòng), khi bị hủy (`OperationCanceledException`) sẽ:
  1. `Kill(entireProcessTree: true)` — diệt cả cây tiến trình con (quan
     trọng vì yt-dlp có thể spawn `ffmpeg` làm con).
  2. Đợi tiến trình thoát hẳn rồi mới ném lại exception hủy.
  - Đây là cơ chế then chốt đảm bảo khi người dùng bấm "Hủy", không để lại
    tiến trình `ffmpeg`/`yt-dlp` zombie chạy ngầm.

### 5.4. `YtDlpOutputParser`

Phân tích các dòng output đặc biệt do `YtDlpDownloadService` yêu cầu
yt-dlp in ra qua `--print`/`--progress-template` (định dạng cố định, dùng
ký tự `|` phân tách, an toàn với tiêu đề/đường dẫn có chứa `|` vì
`Split(..., 3)` hoặc `Split(..., 5)` giới hạn số phần tách):

- `START|<id>|<title>` → `ProgressMessageType.Start`.
- `PROGRESS|<id>|<percent>%|<speed>|<eta>` → `ProgressMessageType.Progress`
  (parse phần trăm dạng số thực, kẹp 0–100).
- `COMPLETE|<id>|<filepath>` → `ProgressMessageType.Complete`, 100%.
- Dòng `WARNING:`/`ERROR:` (có thể có tiền tố `[label] `) → tương ứng
  `Warning`/`Error`.
- Xóa mã màu ANSI escape trước khi parse (regex `AnsiEscapeRegex`).

### 5.5. `YtDlpDurationParser` & `YouTubeApiDurationParser`

- `YtDlpDurationParser`: parse dòng `DURATION|<id>|<seconds>` (dùng khi
  fallback lấy thời lượng bằng yt-dlp + cookie trình duyệt, mỗi video một
  lệnh riêng).
- `YouTubeApiDurationParser`: parse chuỗi ISO-8601 duration
  (`PT#H#M#S`) trả về từ YouTube Data API bằng `XmlConvert.ToTimeSpan`.

### 5.6. `DurationFilterHelper`

Hàm thuần `Matches(durationSeconds, isEnabled, comparison, limitValue,
unit)`:
- Nếu bộ lọc tắt → luôn khớp (`true`).
- Nếu bật mà video chưa có `durationSeconds` → không khớp (`false`) — lý
  do phần "video không có metadata thời lượng bị ẩn" trong README.
- Quy đổi `limitValue` theo `unit` (giây/phút/giờ) rồi so sánh nhỏ hơn/lớn
  hơn.

### 5.7. `RecentVideoFilterHelper`

`TakeMostRecent<T>`: sắp theo (có ngày đăng trước, ngày đăng giảm dần, rồi
theo thứ tự xuất hiện gốc để ổn định) và lấy tối đa N phần tử; N ≤ 0 nghĩa
là lấy tất cả. Dùng chung cho cả `DownloadTabViewModel.ApplyFilters` (lọc
hiển thị) và `YtDlpChannelAnalyzer` (giới hạn số video cần lấy metadata
duration/số video trả về ngay từ lúc phân tích).

## 6. Tầng Service — chi tiết nghiệp vụ

### 6.1. `YtDlpChannelAnalyzer` (phân tích kênh, không tải)

`AnalyzeAsync(channelUri, contentTypes, recentVideoLimit,
resolveMissingDurations, useBrowserCookies, browserName, ct)`:

1. Với **mỗi loại nội dung** đã chọn (Video/Short/Stream), gọi
   `AnalyzeTabAsync` để chạy:
   ```
   yt-dlp --flat-playlist --dump-single-json --ignore-errors
          --encoding utf-8 [--playlist-end N] [--cookies-from-browser X]
          <channel>/<videos|shorts|streams>
   ```
   `--flat-playlist` giúp lấy nhanh metadata cơ bản (không mở từng trang
   video), kết quả là 1 khối JSON duy nhất chứa `entries[]`.
2. Parse JSON (`PlaylistDto`/`PlaylistEntryDto`), gộp entry theo `id` vào
   `Dictionary` chung (loại trùng giữa các tab, ví dụ video xuất hiện ở cả
   tab videos và streams).
3. Với tab Stream: bỏ qua các mục có `live_status` là `is_live` hoặc
   `is_upcoming` (chỉ giữ livestream **đã kết thúc/phát lại**).
4. Xác định tên kênh (`FirstNonEmpty` giữa `channel`, `uploader`, tiêu đề
   playlist đã làm sạch hậu tố " - Videos"/" - Shorts"/" - Live").
5. Dùng `RecentVideoFilterHelper.TakeMostRecent` để giữ tối đa
   `recentVideoLimit` video mới nhất **trên toàn bộ candidate đã gộp**
   (không phải theo từng tab riêng).
6. **Lấy thời lượng còn thiếu** theo thứ tự ưu tiên hai lớp:
   - Lớp 1 — `PopulateMissingDurationsFromYouTubeDataApiAsync`: nếu
     `IYouTubeDataApiDurationResolver.IsConfigured` (có API key), luôn thử
     trước (không phụ thuộc cờ `resolveMissingDurations`/cookie). Trước
     tiên tra cache (`IVideoDurationCache`), sau đó gọi API theo lô tối đa
     50 id/`videos.list?part=contentDetails`. Lỗi API/network/JSON không
     làm hỏng toàn bộ luồng — chỉ log cảnh báo rồi tiếp tục.
   - Lớp 2 — `PopulateMissingDurationsAsync` (chỉ chạy khi
     `resolveMissingDurations == true`, tức bộ lọc thời lượng đang bật):
     với các video **vẫn còn thiếu** duration sau lớp 1, tra cache tiếp,
     rồi nếu `useBrowserCookies` đang bật, gọi **tuần tự từng video một**
     lệnh yt-dlp riêng (`--skip-download --print DURATION|%(id)s|
     %(duration)s --cookies-from-browser`), có **delay ngẫu nhiên
     1000–2000ms** giữa các lần gọi để giảm nguy cơ bị YouTube chặn (rate
     limit). Nếu không bật cookie, chỉ cảnh báo và bỏ qua toàn bộ (không
     gọi yt-dlp từng video vì sẽ luôn thất bại hoặc rất chậm).
   - Video được lấy thành công đều được ghi vào `IVideoDurationCache` để
     lần sau không cần tra lại.
   - Nếu gặp thông điệp đòi xác minh ("Sign in to confirm", "login
     required", "cookies are required") → đánh dấu
     `AuthenticationRequired`, log cảnh báo nhưng **tiếp tục** với video kế
     tiếp thay vì dừng cả batch.
7. Với mỗi candidate còn lại, `CreateDownloadItem` dựng `DownloadItem`:
   - `GetInitialStatus` suy ra trạng thái ban đầu từ trường `availability`
     hoặc tiêu đề đặc biệt của yt-dlp: `private` → `Private`;
     `subscriber_only/premium_only/needs_auth` → `RequiresLogin`;
     `unavailable`/"[Deleted video]" → `Unavailable`; còn lại → `Ready`.
   - Chỉ video `Ready` mới được tự động tick chọn (`IsSelected = true`).
   - Thumbnail ưu tiên trường `thumbnail`, fallback ảnh cuối cùng trong
     mảng `thumbnails[]`.
8. Ghi một snapshot JSON metadata (`IVideoMetadataLogger.WriteAsync`) chứa
   toàn bộ danh sách video vừa phân tích (phục vụ tra cứu/gỡ lỗi thủ công,
   không dùng lại bởi ứng dụng).
9. Trả về `ChannelAnalysisResult(channelName, items)`.

Lỗi khi chạy yt-dlp (không khởi chạy được, JSON rỗng/hỏng) được bọc thành
`ChannelAnalysisException` với thông điệp thân thiện
(`CreateFriendlyAnalysisError` nhận diện các mẫu lỗi phổ biến: kênh không
tồn tại, cần đăng nhập, lỗi mạng).

### 6.2. `YtDlpDownloadService` (tải từng video)

- Dùng `SemaphoreSlim(1,1)` (`_downloadGate`) để đảm bảo **chỉ một** lệnh
  yt-dlp tải chạy tại một thời điểm trong toàn ứng dụng (kể cả nếu về sau
  có nhiều tab cùng bấm tải — tránh làm bão hòa băng thông/CPU và tránh
  rắc rối khi 2 tiến trình cùng ghi 1 file archive).
- `DownloadCoreAsync`:
  1. Tạo thư mục kênh + thư mục loại nội dung (`Videos/Shorts/Streams`).
  2. `BuildStartInfo` dựng câu lệnh yt-dlp đầy đủ:
     ```
     yt-dlp --continue --ignore-errors --no-overwrites --newline
            --windows-filenames --no-simulate --no-playlist
            --encoding utf-8 --ffmpeg-location <ToolsDir>
            --progress-template "download:PROGRESS|%(info.id)s|...''"
            --print "before_dl:START|%(id)s|%(title)s"
            --print "after_move:COMPLETE|%(id)s|%(filepath)s"
            --output "<template>"
            -f <format string theo chất lượng> [--merge-output-format mp4]
            [--download-archive <archive.txt>]
            [--write-thumbnail] [--write-subs --write-auto-subs
             --sub-langs vi.*,en.*]
            [--cookies-from-browser <browser>]
            <video url>
     ```
     - `--continue` cho phép tiếp tục file `.part` dở dang từ lần trước
       (khớp với hành vi khi bị hủy giữa chừng).
     - Chất lượng: `Best → bv*+ba/b`; các mốc `P2160/P1440/P1080/P720/P480`
       → `bv*[height<=N]+ba/b[height<=N]`; `AudioMp3` → dùng
       `--extract-audio --audio-format mp3 --audio-quality 0` (không dùng
       `-f`).
     - Nếu không phải audio, ép `--merge-output-format mp4` (chuẩn hóa
       container sau khi ghép video+audio bằng ffmpeg).
  3. Chạy tiến trình bằng `ProcessHelper.RunAsync`, xử lý từng dòng qua
     `HandleLineAsync`:
     - Nếu khớp định dạng chuẩn (`YtDlpOutputParser`), báo `progress.Report`
       tương ứng; ghi nhận `completedPath` khi có `Complete`, `lastError`
       khi có `Error`.
     - Dòng chứa "has already been recorded in the archive" hoặc "has
       already been downloaded" → đánh dấu `archiveHit = true` (video đã
       có trong `archive.txt`, yt-dlp tự bỏ qua).
     - Dòng bắt đầu `[Merger]`/`[ExtractAudio]`/`[VideoConvertor]` → báo
       `ProgressMessageType.Processing` (giai đoạn ghép/convert bằng
       ffmpeg sau khi tải xong).
     - Dòng lỗi trên stderr chứa "ERROR"/"Sign in"/"unavailable" → cập
       nhật `lastError` dự phòng (trường hợp không khớp parser chuẩn).
     - Mọi dòng đều được log ở mức Debug.
  4. Sau khi tiến trình kết thúc, ưu tiên xác định kết quả theo thứ tự:
     `archiveHit` → `AlreadyDownloaded`; có `completedPath` → `Completed`;
     ngược lại → `MapDownloadError` phân loại `lastError` thành
     `Private/RequiresLogin/Unavailable/Failed` với thông điệp tiếng Việt
     thân thiện tương ứng.
  5. Lỗi khởi chạy tiến trình (IO/quyền/trạng thái không hợp lệ) → trả về
     `DownloadResult(Failed, ...)` thay vì ném exception, để vòng lặp tải ở
     ViewModel không dừng đột ngột theo cách không kiểm soát (dù ViewModel
     vẫn có try/catch bọc ngoài).
  6. Hủy (`OperationCanceledException`) được log rồi ném tiếp để
     `DownloadTabViewModel` xử lý là "Đã hủy".

### 6.3. `DependencyService`

- Xác định `ToolsDirectory` từ cấu hình `Application:ToolsDirectory` (mặc
  định thư mục `Tools` cạnh file thực thi).
- `CheckAsync`: chạy song song về mặt logic tuần tự 3 kiểm tra — mỗi công
  cụ kiểm tra sự tồn tại file rồi chạy `--version`/`-version`, lấy dòng
  output/error đầu tiên làm chuỗi version hiển thị. Không tự tải/cập nhật
  công cụ — chỉ báo thiếu.

### 6.4. `SettingsService`

- Đường dẫn mặc định: `%AppData%\YouTubeChannelDownloader\settings.json`.
- Đọc: nếu file không tồn tại hoặc JSON hỏng/không truy cập được → trả về
  `AppSettings` mặc định (không throw, luôn có cấu hình dùng được).
- Ghi: **ghi an toàn kiểu atomic** — ghi vào file tạm
  (`settings.json.<guid>.tmp`, `FileMode.CreateNew` nên không đè file tạm
  của tiến trình khác) rồi `File.Move(..., overwrite: true)` để hoán đổi,
  tránh hỏng file cấu hình nếu ứng dụng bị tắt đột ngột giữa lúc ghi. Dọn
  file tạm ở khối `finally` dù thành công hay thất bại.

### 6.5. `ArchiveService`

- Đường dẫn archive: `<Output>\<Channel>\archive.txt` (định dạng chuẩn của
  yt-dlp: mỗi dòng `<extractor> <id>`).
- `ReadVideoIdsAsync`: đọc file với `FileShare.ReadWrite` (yt-dlp có thể
  đang ghi đồng thời), tách theo khoảng trắng/tab, lấy token cuối cùng mỗi
  dòng làm video id. Lỗi IO/quyền/đường dẫn không hợp lệ chỉ cảnh báo, trả
  về tập rỗng thay vì làm hỏng luồng phân tích.

### 6.6. `FileAppLogger`

- Mỗi lần chạy ứng dụng tạo 1 file log mới:
  `%LocalAppData%\YouTubeChannelDownloader\Logs\app-<timestamp>-<pid>.log`.
  Nếu không tạo được thư mục ưu tiên (quyền...), fallback sang
  `%Temp%\YouTubeChannelDownloader\Logs`.
- Ghi UTF-8 không BOM, `AutoFlush = true` (không mất log khi crash).
- Chuẩn hóa message: loại bỏ `\r`/`\n` để mỗi entry chắc chắn nằm trên một
  dòng log.
- Phát sự kiện `EntryLogged` cho mọi listener (mỗi `DownloadTabViewModel`
  lắng nghe để đổ vào ô Log của tab đó) — nghĩa là **log là toàn cục**,
  hiển thị ở tất cả các tab như nhau (không lọc theo tab/video).
- Khóa `_syncRoot` bảo vệ `StreamWriter` dùng chung giữa nhiều luồng (nhiều
  tiến trình con ghi log đồng thời).

### 6.7. `FileVideoDurationCache`

- File: `%LocalAppData%\YouTubeChannelDownloader\Cache\durations.json`
  (`Dictionary<videoId, seconds>`).
- Nạp một lần khi khởi tạo service (singleton), lọc bỏ giá trị âm/NaN/
  không hữu hạn hoặc khóa rỗng.
- `TryGet`: đọc từ dictionary trong bộ nhớ (có khóa `lock`).
- `StoreAsync`: bỏ qua nếu giá trị mới gần như không đổi (`< 0.001` giây
  khác biệt, tránh ghi đĩa thừa), ghi atomic tương tự `SettingsService`
  (file tạm + `File.Move overwrite`) bảo vệ bởi `SemaphoreSlim` riêng cho
  việc ghi để tránh nhiều lần ghi đè nhau.

### 6.8. `FileVideoMetadataLogger`

- Mỗi lần phân tích kênh xong, ghi 1 file JSON mới (không đè):
  `%LocalAppData%\YouTubeChannelDownloader\Logs\Metadata\
  metadata-<timestamp>-<guid>.json`, chứa toàn bộ danh sách video +
  metadata + trạng thái tại thời điểm phân tích. Ghi atomic (file `.tmp`
  rồi `Move`). Lỗi ghi chỉ cảnh báo, không chặn luồng chính.

### 6.9. `YouTubeDataApiDurationResolver`

- Đọc API key ưu tiên biến môi trường `YOUTUBE_DATA_API_KEY`, sau đó
  `YouTubeDataApi:ApiKey` trong `appsettings.json`. `IsConfigured` = có key
  không rỗng.
- `ResolveAsync`: loại trùng id, chia lô tối đa 50 id/request (giới hạn của
  API `videos.list`), gọi `GET https://www.googleapis.com/youtube/v3/
  videos?part=contentDetails&id=...&key=...`. Parse `contentDetails.
  duration` (ISO-8601) bằng `YouTubeApiDurationParser`.
- Lỗi HTTP không thành công → ném `YouTubeDataApiException` kèm thông điệp
  trích từ JSON lỗi trả về (không làm lộ API key trong thông điệp).
- Không cần cookie trình duyệt — chỉ cần API key hợp lệ, phù hợp cho dữ
  liệu công khai.

### 6.10. `FileDialogService`

Bọc các API WPF/Win32 cần giao diện: `OpenFolderDialog` (WPF hiện đại, có
`InitialDirectory`), `Clipboard` (chỉ đọc text Unicode), mở Explorer qua
`Process.Start(UseShellExecute = true)`, và 3 hộp thoại `MessageBox`
(Warning/Error/Info).

## 7. Lưu trữ dữ liệu trên đĩa (tổng hợp)

| Dữ liệu | Đường dẫn | Ghi chú |
|---|---|---|
| Cấu hình | `%AppData%\YouTubeChannelDownloader\settings.json` | Không chứa cookie/token/mật khẩu; không lưu danh sách video/log/tiến trình |
| Log ứng dụng | `%LocalAppData%\YouTubeChannelDownloader\Logs\app-*.log` | 1 file/lần chạy |
| Metadata JSON | `...\Logs\Metadata\metadata-*.json` | 1 file/lần phân tích kênh |
| Cache thời lượng | `...\Cache\durations.json` | `videoId → seconds`, dùng chung mọi kênh |
| Archive yt-dlp | `<Output>\<Kênh>\archive.txt` | Chuẩn `--download-archive` của yt-dlp |
| Nội dung tải về | `<Output>\<Kênh>\Videos\|Shorts\|Streams\` | Tên file: `YYYY-MM-DD - <tiêu đề ≤140 byte>.<ext>` |

`AppSettings` (mô hình lưu):

- `SchemaVersion` (hiện = 2), `SelectedTabIndex`, `Tabs: List<
  DownloadTabSettings>` — mỗi phần tử tương ứng một tab với đầy đủ: URL
  kênh, thư mục ra, chất lượng, các cờ tải (video/short/stream/thumbnail/
  phụ đề/bỏ qua đã tải), cookie trình duyệt + tên trình duyệt, cấu hình bộ
  lọc thời lượng, giới hạn video gần nhất.
- Các trường "legacy" phẳng ở cấp `AppSettings` (không nằm trong `Tabs`)
  được giữ lại và luôn đồng bộ theo tab đang chọn mỗi lần lưu, chỉ để các
  bản build cũ hơn (single-tab) vẫn đọc được file settings mới mà không
  crash — khi nạp, nếu `Tabs` rỗng, `GetSavedTabs()` sẽ tự dựng 1 tab từ
  các trường phẳng này.

## 8. Giao diện (`MainWindow.xaml`)

- Thanh trên cùng: ô nhập/tên tab (`TitleInput` + `ConfirmTitleCommand`),
  nút "Lưu cài đặt" (`SaveSettingsCommand`), nút "+ Thêm chủ đề"
  (`AddTabCommand`), danh sách tab (`ItemsSource="{Binding Tabs}"`) với nút
  đóng từng tab (bind `CloseTabCommand` qua `RelativeSource
  AncestorType=Window` vì DataContext của item template là
  `DownloadTabViewModel`, không phải `MainViewModel`).
- Mỗi tab hiển thị (bind vào `DownloadTabViewModel` đang chọn):
  - Ô URL kênh + nút dán URL (`PasteUrlCommand`) + chọn thư mục
    (`BrowseOutputDirectoryCommand`).
  - `ComboBox` chất lượng (`QualityOptions`) và trình duyệt
    (`BrowserOptions`).
  - Cấu hình bộ lọc thời lượng: `ComboBox` so sánh
    (`DurationComparisonOptions`) + đơn vị (`DurationUnitOptions`).
  - Các nút hành động: "Phân tích" (`AnalyzeChannelCommand`), "Tải"
    (`StartDownloadCommand`), "Hủy" (`CancelDownloadCommand`), "Mở thư
    mục" (`OpenOutputDirectoryCommand`).
  - Nút "Chọn tất cả"/"Bỏ chọn" (`SelectAllCommand`/`UnselectAllCommand`).
  - `DataGrid` (`ItemsSource="{Binding FilteredVideoItems}"`) với cột:
    Chọn (checkbox), Thumbnail, Tiêu đề, Video ID, Loại, Thời lượng, Ngày
    đăng, Tiến trình (progress bar), Tốc độ, ETA, Trạng thái, Thông báo
    lỗi.
  - Khung log cuối tab (`ItemsSource="{Binding Logs}"`) + nút "Xóa log"
    (`ClearLogCommand`).

Toàn bộ binding dùng đúng tên thuộc tính/lệnh đã liệt kê ở mục 4 — UI không
chứa logic nghiệp vụ, chỉ hiển thị/raise lệnh.

## 9. Kiểm thử logic (`MultiDownloadVideoYoutube.LogicChecks`)

Một console app độc lập (không dùng framework test) đóng vai trò bộ kiểm
tra hồi quy nhanh, chạy bằng:

```powershell
dotnet run --project .\MultiDownloadVideoYoutube.LogicChecks\MultiDownloadVideoYoutube.LogicChecks.csproj
```

`Program.cs` định nghĩa một mảng `(tên, hàm kiểm tra)` chạy tuần tự, in
`PASS`/`FAIL` từng cái, thoát mã 1 nếu có lỗi. Có thể tự giả lập chính nó
làm "fake yt-dlp" (`TryRunAsFakeYtDlp`) để test `ProcessHelper`/luồng tải
mà không cần yt-dlp thật. Các nhóm kiểm tra bao gồm: chuẩn hóa URL hợp
lệ/không hợp lệ, parser tiến trình (`PROGRESS/START/COMPLETE`) và tính an
toàn khi có dấu `|` trong tiêu đề, parser thời lượng (yt-dlp và YouTube
Data API), resolver YouTube Data API (đơn vị + tích hợp với analyzer),
giới hạn số video theo playlist, bỏ qua tra cứu duration không cần thiết,
tiếp tục tra cứu sau lỗi xác thực (bot verification), cache thời lượng
(bộ nhớ + đĩa), ghi log metadata JSON, fallback khi thiếu duration, an
toàn tên file, bộ lọc video gần nhất, bộ lọc thời lượng, settings hỏng vẫn
dùng được mặc định, thiếu dependency, và hủy tiến trình (đảm bảo cây tiến
trình con bị kill).

## 10. Build, chạy, cấu hình

- SDK: `.NET 8.0.416` (khóa bởi `global.json`, cho phép roll-forward patch
  8.0 mới hơn). Target `net8.0-windows`, `UseWPF=true`,
  `AssemblyName=YouTubeChannelDownloader`.
- Gói NuGet: `CommunityToolkit.Mvvm 8.4.0`,
  `Microsoft.Extensions.Configuration(.Json) 8.0.x`,
  `Microsoft.Extensions.DependencyInjection 8.0.1`.
- `Tools\*` và `appsettings.json` được copy sang thư mục output/publish
  (`CopyToOutputDirectory=PreserveNewest`).
- Cấu hình YouTube Data API (tùy chọn, khuyến nghị): biến môi trường
  `YOUTUBE_DATA_API_KEY` (ưu tiên) hoặc khóa `YouTubeDataApi:ApiKey` trong
  `appsettings.json`. Không có key vẫn dùng được ứng dụng — chỉ mất khả
  năng lấy duration nhanh qua API, sẽ fallback yt-dlp+cookie nếu bật lọc
  thời lượng.
- Thư mục `publish\win-x64\` chứa bản build self-contained đã publish sẵn
  (không phải mã nguồn) — không cần chỉnh sửa trực tiếp.

## 11. Các nguyên tắc thiết kế xuyên suốt đáng chú ý

1. **Không bao giờ song song hóa việc tải** — `SemaphoreSlim(1,1)` trong
   `YtDlpDownloadService` và vòng lặp `foreach` tuần tự trong
   `DownloadTabViewModel` đảm bảo tại một thời điểm toàn ứng dụng chỉ có
   tối đa một lệnh yt-dlp tải video đang chạy, dù có nhiều tab.
2. **Ghi file cấu hình/cache luôn atomic** (file tạm + `File.Move
   overwrite: true`) để không bao giờ để lại file JSON dở dang, hỏng.
3. **Hủy tiến trình luôn kill cả cây tiến trình con** (`ProcessHelper`),
   tránh để lại `ffmpeg`/`yt-dlp` ngầm sau khi người dùng bấm Hủy hoặc
   đóng ứng dụng.
4. **Không lưu trữ bất cứ thông tin nhạy cảm nào** (cookie, token, mật
   khẩu) — cookie trình duyệt chỉ truyền tên trình duyệt cho tham số
   `--cookies-from-browser` của yt-dlp tại thời điểm chạy.
5. **Suy giảm nhẹ nhàng (graceful degradation)**: thiếu API key → fallback
   yt-dlp; thiếu duration → tự tắt bộ lọc thay vì chặn hiển thị danh sách;
   lỗi đọc archive/cache/settings → dùng giá trị mặc định/rỗng thay vì
   crash toàn ứng dụng.
6. **Log là nguồn sự thật chung** cho debug: mọi service đều log qua
   `IAppLogger`, UI chỉ là một "subscriber" của luồng log đó.
