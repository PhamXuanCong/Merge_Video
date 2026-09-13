# VideoMergeTool — Tổng quan dự án

> Tài liệu này tổng hợp kiến trúc, logic nghiệp vụ và các quy ước quan trọng của dự án để tra cứu nhanh trong các phiên làm việc sau. Đọc cùng với `AGENTS.md` (nguồn quy tắc gốc) khi cần đối chiếu.

## 1. Ứng dụng này làm gì

VideoMergeTool là một ứng dụng **WPF (.NET, Windows only)** giúp ghép mỗi video MP4 đầu vào với một video "đồng hành" (companion video) được đóng gói sẵn, tạo ra video dọc (mặc định 1080×1920) gồm hai video đặt cạnh nhau.

Luồng chính:

1. Người dùng chọn **một thư mục input** chứa các file MP4.
2. Ứng dụng quét đệ quy toàn bộ file `*.mp4` trong thư mục đó (bỏ qua các thư mục con tên `output` và `processed`).
3. Với mỗi video input, ứng dụng chọn một video đồng hành từ `Assets/CompanionVideos` (241 file MP4 nội dung ASMR/satisfying được đóng gói sẵn cùng ứng dụng).
4. Dùng FFmpeg ghép video input và video đồng hành theo chiều ngang (`hstack`, mặc định) hoặc chiều dọc (`vstack`) thành một khung 1080×1920.
5. Ghi kết quả vào `<thư-mục-input>/output`, giữ nguyên cấu trúc thư mục tương đối của input.
6. Tuỳ theo cấu hình, xoá / di chuyển / giữ lại file input gốc sau khi xử lý thành công.

## 2. Kiến trúc & ràng buộc dependency

Solution `VideoMergeTool.sln` gồm 5 project:

```
src/VideoMergeTool.App             WPF UI + ViewModel (net8.0-windows, WinExe)
src/VideoMergeTool.Core            Model, Enum, Interface — không phụ thuộc project khác (net8.0)
src/VideoMergeTool.Infrastructure  Cài đặt filesystem, FFmpeg, FFprobe (net8.0, chỉ ref Core)
tests/VideoMergeTool.Core.Tests
tests/VideoMergeTool.Infrastructure.Tests
```

Quy tắc kiến trúc bắt buộc (theo `AGENTS.md`):

- **Core không được reference App hoặc Infrastructure.**
- **Infrastructure không được reference App.**
- Không đặt lệnh FFmpeg hay code filesystem trong ViewModel — toàn bộ logic xử lý nằm ở Infrastructure, ViewModel chỉ gọi qua interface của Core.
- Dùng **constructor injection** (Microsoft.Extensions.Hosting `IHost` + DI container, khai báo trong `App.xaml.cs`).
- Dùng `CancellationToken` cho mọi luồng async.
- Dùng `ProcessStartInfo.ArgumentList` (không nối chuỗi tham số có dấu ngoặc kép) khi gọi FFmpeg/FFprobe.
- Dùng invariant culture khi format số cho tham số FFmpeg.

## 3. Core — Model, Enum, Interface

### Enums (`src/VideoMergeTool.Core/Enums`)

| Enum | Giá trị | Ý nghĩa |
|---|---|---|
| `PairingMode` | `Random`, `RandomWithoutImmediateRepeat` (mặc định), `Sequential` | Cách chọn video đồng hành cho mỗi input |
| `MergeLayout` | `Horizontal` (mặc định), `Vertical` | Ghép cạnh nhau (hstack) hay chồng lên nhau (vstack) |
| `X264Preset` | `Medium`, `Fast`, `Faster`, `VeryFast` (mặc định) | Preset tốc độ/nén — dùng cho cả libx264 lẫn NVENC (mapping khác nhau, xem mục 4) |
| `VideoEncoder` | `Cpu` (mặc định, libx264), `NvidiaGpu` (h264_nvenc) | Encode bằng CPU hay bằng GPU NVIDIA |
| `SourceFileAction` | `Keep`, `MoveToProcessed`, `Delete` (mặc định) | Xử lý file input sau khi ghép thành công |
| `ExistingOutputAction` | `Skip` (mặc định), `Overwrite`, `CreateUniqueName` | Xử lý khi output đã tồn tại |
| `VideoTaskStatus` | `Pending → ReadingMetadata → Processing → Validating → Completed` / `Skipped` / `Failed` / `Cancelled` | Trạng thái của một task ghép video |
| `ThemePreference` | `System` (mặc định), `Light`, `Dark` | Giao diện sáng/tối; `System` bám theo theme của Windows |

### Models chính (`src/VideoMergeTool.Core/Models`)

- **`ProcessingOptions`** (record, immutable): toàn bộ tuỳ chọn xử lý — `PairingMode`, `MergeLayout`, `X264Preset`, `VideoEncoder` (mặc định `Cpu`), `CpuThreadLimit` (mặc định = `Clamp(ProcessorCount/2, 1, 8)`), `SourceFileAction`, `ExistingOutputAction`, `OutputWidth` (1080), `OutputHeight` (1920), `LeftRegionWidth` (= `OutputWidth/2`, tính toán), `DurationTolerance` (1 giây).
- **`VideoMergeTask`** (class, mutable — dùng để report tiến trình): `InputFile`, `CompanionFile`, `OutputFile`, `TemporaryOutputFile`, `InputDuration`, `Status`, `Progress` (0–1), `ErrorMessage`.
- **`VideoFileInfo`** (record): `FullPath`, `FileName`, `RelativePath` (so với thư mục input gốc — dùng để giữ cấu trúc thư mục khi ghi output).
- **`VideoMetadata`** (record): `Duration`, `HasVideo`, `HasAudio`, `Width?`, `Height?` — kết quả parse từ FFprobe.
- **`ApplicationPaths`**: tính toán mọi đường dẫn tài nguyên đóng gói từ **`AppContext.BaseDirectory`** (không dùng thư mục làm việc hiện tại — quan trọng để app chạy portable, không phụ thuộc nơi được launch):
  - `Tools/ffmpeg.exe`, `Tools/ffprobe.exe`
  - `Assets/CompanionVideos`
  - `Licenses/`
  - `README.txt`
- **`UserSettings`** (record): `InputFolder`, `MergeLayout`, `X264Preset`, `VideoEncoder`, `CpuThreadLimit`, `PairingMode`, `SourceFileAction`, `ExistingOutputAction`, `ThemePreference`, `RecentFolders` (`IReadOnlyList<string>`, tối đa 8, mới nhất trước) — đúng bộ tuỳ chọn mà UI cho chỉnh, được lưu lại khi đóng cửa sổ. Vì `RecentFolders` là collection, equality mặc định của `record` sẽ so theo tham chiếu (làm hỏng round-trip JSON) — `UserSettings` tự viết `Equals`/`GetHashCode` để so sánh theo nội dung (`SequenceEqual`) thay vì để trình biên dịch tự sinh.
- **`FFmpegExecutionResult`**, **`OutputValidationResult`**: kết quả thực thi FFmpeg / kết quả validate output.
- **`ProcessingSummary`**: tổng hợp số lượng task theo từng trạng thái (Completed/Failed/Skipped/Cancelled) từ danh sách `VideoMergeTask`.

### Interfaces (`src/VideoMergeTool.Core/Interfaces`)

`IInputVideoScanner`, `ICompanionVideoProvider`, `IFFprobeService`, `IFFmpegService`, `IOutputValidator`, `IOutputPathService`, `IVideoProcessingCoordinator` — mỗi interface có đúng một cài đặt trong Infrastructure (xem mục 4).

## 4. Infrastructure — logic nghiệp vụ chi tiết

### `InputVideoScanner` (implements `IInputVideoScanner`)

- Quét đệ quy thư mục input, tìm `*.mp4`.
- **Loại trừ** các thư mục con tên `output` hoặc `processed` (so sánh không phân biệt hoa/thường) — tránh quét lại video đã xử lý hoặc video output.
- Loại trừ mọi file có hậu tố `.processing.mp4` (file tạm đang được FFmpeg ghi).
- Trả về `VideoFileInfo` với `RelativePath` tính từ thư mục gốc — dùng để tái tạo cấu trúc thư mục trong `output/`.
- Nếu thư mục input không tồn tại → ném `DirectoryNotFoundException` ngay (không im lặng trả về danh sách rỗng, tránh việc gõ sai đường dẫn bị hiểu nhầm là "không có video nào").
- Toàn bộ việc duyệt cây thư mục chạy trên thread pool (`Task.Run`) — trước đây nó chạy đồng bộ trên UI thread và làm treo cửa sổ với thư mục lớn.
- Dùng `EnumerationOptions { IgnoreInaccessible = true }` và bắt lỗi theo từng thư mục: một thư mục con bị từ chối quyền truy cập chỉ bị bỏ qua, không làm hỏng cả lần quét.

### `CompanionVideoProvider` (implements `ICompanionVideoProvider`)

- Liệt kê toàn bộ `*.mp4` trong `Assets/CompanionVideos` (đệ quy), sắp xếp theo `StringComparer.OrdinalIgnoreCase` để có thứ tự ổn định.
- Nếu thư mục không tồn tại → trả về danh sách rỗng (không throw) — validate sự tồn tại được xử lý riêng ở `ApplicationPathValidator` lúc khởi động app.
- **Cache** danh sách sau lần liệt kê đầu tiên thành công: companion video là tài nguyên chỉ-đọc trong suốt vòng đời process, không cần quét lại 241 file cho mỗi lần chạy batch. Danh sách rỗng thì không cache (để thư mục xuất hiện muộn vẫn được nhận ra).

### `OutputPathService` (implements `IOutputPathService`)

- `GetOutputFilePath`: `<inputFolder>/output/<RelativePath của input>`.
- `GetTemporaryOutputFilePath`: chèn hậu tố `.processing` trước phần mở rộng, ví dụ `abc.mp4` → `abc.processing.mp4`. FFmpeg luôn ghi ra file tạm này trước, không ghi trực tiếp vào file output cuối cùng.
- `GetProcessedFilePath`: `<inputFolder>/processed/<RelativePath của input>` — dùng khi `SourceFileAction = MoveToProcessed`.

### `FFprobeService` (implements `IFFprobeService`)

- Chạy `ffprobe.exe` với `-show_entries format=duration:stream=codec_type,width,height -of json`.
- Parse JSON output (method `ParseMetadata` là `static` — unit-testable không cần chạy FFprobe thật).
- Trả về `VideoMetadata`: có stream video không, có stream audio không, width/height (của stream video đầu tiên), duration (parse `double` theo invariant culture).
- Hủy tiến trình (`Kill(entireProcessTree: true)`) khi `CancellationToken` bị hủy.
- Ném `InvalidOperationException` nếu exit code khác 0.

### `FFmpegCommandBuilder` (class thuần, không interface — được DI như singleton)

Xây dựng danh sách argument cho FFmpeg (dùng `ArgumentList`, không nối chuỗi). Điểm mấu chốt:

- **Bắt buộc** `task.InputDuration` phải được set trước (đọc từ FFprobe) — nếu chưa có sẽ throw.
- Input 0 = video input của người dùng; Input 1 = video companion, được lặp vô hạn bằng `-stream_loop -1` (vì companion thường ngắn hơn input, cần loop để phủ hết thời lượng).
- **Filter Horizontal** (`hstack`): mỗi video được `scale` theo `force_original_aspect_ratio=increase` rồi `crop` về đúng `LeftRegionWidth × OutputHeight` (mỗi bên chiếm nửa chiều rộng, ví dụ 540×1920 mỗi bên với output 1080×1920), sau đó `setsar=1`, rồi ghép `hstack=inputs=2`.
- **Filter Vertical** (`vstack`): tương tự nhưng scale/crop theo `OutputWidth × (OutputHeight/2)`, ghép `vstack=inputs=2`.
- Map `[v]` (video đã ghép) và `0:a?` (audio của input gốc, nếu có — dấu `?` nghĩa là optional).
- **Video encoder** (`GetVideoCodecArguments`, chọn theo `ProcessingOptions.VideoEncoder`):
  - `Cpu` (mặc định): `-c:v libx264 -threads <CpuThreadLimit> -preset <x264 preset>` — chạy được trên mọi máy, chỉ dùng CPU.
  - `NvidiaGpu`: `-c:v h264_nvenc -preset <nvenc preset>` — cần GPU NVIDIA + driver hỗ trợ NVENC. **Không tự phát hiện phần cứng trước khi chạy**: nếu máy không có GPU NVIDIA phù hợp (hoặc driver quá cũ so với bundled FFmpeg), `h264_nvenc` sẽ làm FFmpeg exit khác 0 ngay khi mở encoder, và lỗi đó tự nhiên đi theo đường xử lý lỗi sẵn có (task `Failed`, `errorOutput` hiện ở cột Details, ví dụ *"Driver does not support the required nvenc API version"* hoặc *"no NVENC capable devices found"*) — không cần logic riêng.
  - `X264Preset` (nhãn UI "Tốc độ nén") dùng chung cho cả hai encoder, map sang hai thang khác nhau trong `GetX264Preset`/`GetNvencPreset`: `VeryFast→veryfast/p1`, `Faster→faster/p2`, `Fast→fast/p4`, `Medium→medium/p6` (p1 = nhanh nhất/chất lượng thấp nhất, p7 = chậm nhất/chất lượng tốt nhất theo thang preset của NVENC).
- **`-pix_fmt yuv420p`**: bắt buộc pixel format tương thích rộng, dùng cho cả hai encoder. Nếu không có, pixel format sẽ đi theo source, và một input 10-bit hoặc 4:2:2 sẽ tạo ra file H.264 mà phần lớn trình phát và luồng upload từ chối.
- Trước khi build argument, `EnsureEncodableDimensions` kiểm tra mỗi vùng ghép phải có chiều rộng/chiều cao **chẵn** và ≥ 2 (yêu cầu của yuv420p) — báo lỗi rõ ràng ngay thay vì để FFmpeg fail giữa batch.
- Audio encoder: luôn `aac`.
- `-t <duration>`: cắt output bằng đúng thời lượng của **input gốc** (không phải companion) — vì companion được loop vô hạn.
- `-threads`, `-filter_threads`, `-filter_complex_threads`: đều giới hạn theo `CpuThreadLimit` — filter (scale/crop/stack) vẫn chạy trên CPU dù chọn encoder GPU, nên vẫn cần giới hạn luồng cho bước này. `h264_nvenc` không nhận `-threads` riêng (NVENC không có "threading capability" ở mức encoder) nên nhánh GPU không thêm cờ đó cho `-c:v`.
- `-progress pipe:1 -nostats`: xuất tiến trình dạng `key=value` qua stdout để `FFmpegService` đọc.
- Output ghi vào `task.TemporaryOutputFile` (file `.processing.mp4`), **không ghi trực tiếp vào output cuối**.

### `FFmpegService` (implements `IFFmpegService`)

- Chạy `ffmpeg.exe` với argument từ `FFmpegCommandBuilder`.
- Đọc stdout dòng theo dòng, tìm `out_time_us=<microseconds>`, tính `progress = microseconds / (inputDuration × 1000)` (clamp 0–1), report qua `IProgress<double>`. Chỉ report khi giá trị tăng ≥ 0,5% — FFmpeg gửi tiến trình dày hơn nhiều so với mức một progress bar có thể vẽ lại có ích, và mỗi lần report là một lần post lên UI thread.
- Đọc song song stderr nhưng chỉ **giữ lại 20 dòng cuối** (ring buffer) làm `errorOutput`: đủ để chẩn đoán lỗi, không ôm hàng megabyte log của một file hỏng vào bộ nhớ và vào cột Details trên UI.
- Mọi nhánh lỗi đều `await` lại hai task đọc stream để không có exception bị bỏ quên (unobserved), và luôn kill process trước khi ném lại.
- Khi FFmpeg exit code ≠ 0, khi bị cancel, hoặc khi có exception: **luôn xoá file `.processing.mp4` tạm** (an toàn — không để lại rác).
- Khi cancel: kill toàn bộ process tree (`Kill(entireProcessTree: true)`) rồi ném lại `OperationCanceledException`.

### `OutputValidationService` (implements `IOutputValidator`)

Validate file output tạm **trước khi** commit thành file cuối cùng và trước khi đụng vào file input gốc:

1. File `.processing.mp4` phải tồn tại.
2. FFprobe đọc được metadata, `HasVideo == true`.
3. `|Duration(output) - Duration(input)| <= DurationTolerance` (mặc định 1 giây).

Trả về `OutputValidationResult(IsValid, Metadata, FailureReason)`.

### `UserSettingsService` (implements `IUserSettingsService`)

- Lưu `%AppData%/VideoMergeTool/settings.json`.
- Enum được ghi dưới dạng **tên** (`JsonStringEnumConverter`) cho dễ đọc và không vỡ khi thứ tự enum thay đổi; converter vẫn đọc được dạng số mà bản cũ đã ghi.
- `Load` lỗi (thiếu file, JSON hỏng, không có quyền) → trả về `UserSettings` mặc định.
- `Save` là **best effort**: nó chạy trong lúc cửa sổ đang đóng, nên một vị trí không ghi được không được phép biến thành crash lúc thoát app.

### `VideoProcessingService` (implements `IVideoProcessingCoordinator`) — điều phối toàn bộ pipeline

Đây là class trung tâm, gọi lần lượt: `InputVideoScanner` → `CompanionVideoProvider` → (mỗi input) `FFprobeService` → `FFmpegService` → `OutputValidationService` → di chuyển file → xử lý file gốc.

Luồng `ProcessAsync` cho từng input:

1. Quét input, lấy danh sách companion. Nếu không có companion nào → throw ngay (`InvalidOperationException`).
2. Với mỗi input (dừng vòng lặp nếu đã bị cancel):
   a. Chọn companion theo `PairingMode` (xem bên dưới).
   b. Tính output path; nếu `ExistingOutputAction = Skip` và output đã tồn tại → đánh dấu `Skipped`, **không xử lý**, chuyển sang input tiếp theo.
   c. Gọi `ProcessTaskAsync`:
      - `Status = ReadingMetadata` → đọc metadata input bằng FFprobe. Nếu input không có video stream → throw.
      - `Status = Processing` → tạo thư mục output, xoá file `.processing.mp4` cũ nếu còn sót, gọi FFmpeg merge (progress được report liên tục qua callback).
      - Nếu FFmpeg fail → throw với nội dung `errorOutput` (hoặc "FFmpeg exited with code X" nếu rỗng).
      - `Status = Validating` → gọi `OutputValidationService`. Nếu invalid → throw với `FailureReason`.
      - **Chỉ khi mọi bước trên pass**: `File.Move(temp, output, overwrite: true)` để commit file tạm thành file output chính thức.
      - Áp dụng `SourceFileAction` lên file input gốc (**chỉ sau khi output đã được commit thành công**). Nếu bước này fail (ví dụ file input đang bị khoá bởi tiến trình khác), task **vẫn là `Completed`** và thông báo được ghi vào `ErrorMessage` dưới dạng cảnh báo — vì output đã được commit rồi, báo `Failed` sẽ che mất một output tốt và khiến người dùng chạy lại vô ích.
      - `Status = Completed`, `Progress = 1`.
   d. Bắt exception: nếu do cancel → `Status = Cancelled`, xoá file tạm, dừng vòng lặp (`break`). Nếu lỗi khác → `Status = Failed`, lưu `ErrorMessage`, xoá file tạm, **tiếp tục** sang input kế tiếp (không dừng toàn bộ batch).
3. Trả về `ProcessingSummary` chứa toàn bộ task (kể cả các task chưa kịp chạy nếu bị cancel giữa chừng — chúng giữ status `Pending`).

**Đây chính là ràng buộc an toàn cốt lõi của ứng dụng** (khớp với mục "Safety" trong `AGENTS.md`): không bao giờ đụng đến (xoá/di chuyển) file input gốc trừ khi FFmpeg đã chạy thành công, FFprobe đã xác nhận output hợp lệ (có video stream, đúng thời lượng ±1s), và file tạm đã được đổi tên thành công sang file output cuối cùng.

Logic chọn companion (`SelectCompanion`):

- `Sequential`: xoay vòng theo index tăng dần; bộ đếm được `% count` ngay khi tăng nên không bao giờ tràn thành index âm trong batch dài.
- `Random`: chọn ngẫu nhiên hoàn toàn mỗi lần.
- `RandomWithoutImmediateRepeat` (mặc định): bốc một index trong `count - 1` khả năng rồi dịch qua vị trí của companion trước đó. Phân phối giống hệt cách bốc-lại-đến-khi-khác, nhưng **luôn kết thúc** thay vì lặp `do…while` không có giới hạn.

Logic `GetOutputPath` khi `ExistingOutputAction = CreateUniqueName`: nếu file đã tồn tại, thêm hậu tố ` (1)`, ` (2)`, … cho đến khi tìm được tên chưa dùng.

`TryApplySourceFileAction` (trả về `null` khi thành công, hoặc chuỗi cảnh báo khi thất bại):
- `Keep`: không làm gì.
- `MoveToProcessed`: di chuyển input sang `<inputFolder>/processed/<RelativePath>` (tạo thư mục nếu cần).
- `Delete` (mặc định): xoá file input.

### `ApplicationPathValidator`

Chạy **trước khi hiện MainWindow** (gọi trong `App.OnStartup`). Kiểm tra:
1. `Tools/ffmpeg.exe` tồn tại.
2. `Tools/ffprobe.exe` tồn tại.
3. Thư mục `Assets/CompanionVideos` tồn tại.
4. Có ít nhất 1 file `.mp4` (so khớp phần mở rộng không phân biệt hoa/thường) trong thư mục đó.

Nếu fail bất kỳ điều kiện nào → ném `InvalidOperationException` với thông báo **tiếng Việt**, App hiện `MessageBox` lỗi rồi `Shutdown(-1)` — không hiện MainWindow, không lộ stack trace cho người dùng.

## 5. App — WPF UI

- **`App.xaml.cs`**: khởi tạo `IHost` (Microsoft.Extensions.Hosting), đăng ký toàn bộ service ở mục 4 làm **singleton** qua DI. Chạy `ApplicationPathValidator.Validate()` trước khi tạo `MainWindow`. Đường dẫn tài nguyên luôn resolve từ `AppContext.BaseDirectory` — không phụ thuộc working directory hiện tại (bắt buộc để chạy được ở dạng portable folder, double-click từ bất kỳ đâu).
### 5.1 Giao diện sáng/tối

- **`Themes/Light.xaml`** và **`Themes/Dark.xaml`**: chỉ chứa bảng màu (`SolidColorBrush`), **cùng bộ key giống hệt nhau** — đây là điều kiện bắt buộc vì hai dictionary này được hoán đổi lúc chạy.
- **`Themes/Controls.xaml`**: toàn bộ style/template. Mọi tham chiếu màu ở đây phải là **`DynamicResource`**, không được `StaticResource` — nếu không, màu sáng sẽ bị "đóng băng" vào template và đổi theme sẽ không có tác dụng.
- **`App.xaml`** merge hai dictionary theo thứ tự: **index 0 là bảng màu** (bị thay lúc chạy), index 1 là `Controls.xaml`.
- **`Theming/ThemeManager.cs`**: `Apply(ThemePreference)` thay `MergedDictionaries[0]`. Với `System`, đọc registry `HKCU\...\Themes\Personalize\AppsUseLightTheme` và lắng nghe `SystemEvents.UserPreferenceChanged` để đổi theo Windows **ngay lúc đang chạy** (event này bắn trên thread riêng nên phải `Dispatcher.BeginInvoke`).
- **`Theming/WindowChrome.cs`**: gọi `DwmSetWindowAttribute` với `DWMWA_USE_IMMERSIVE_DARK_MODE` (20, fallback 19 cho Windows 10 cũ) để **thanh tiêu đề cũng tối**. Không có bước này, app tối + title bar trắng trông như lỗi chứ không như dark theme.
- Nút "Theme: Auto/Light/Dark" ở góc trên phải xoay vòng ba trạng thái, lưu vào `UserSettings`.
- **Lưu ý**: màu trạng thái (Done/Failed/…) được đặt bằng `DataTrigger` + `DynamicResource` trong style `StatusLabel` / `RowProgressBar`, **không dùng `IValueConverter`** — converter trả về một `Brush` cố định mà binding không đánh giá lại khi đổi theme, nên hàng sẽ giữ nguyên màu cũ.
- Hộp thoại `MessageBox` là dialog Win32 gốc nên **vẫn nền sáng** kể cả ở chế độ tối; đây là giới hạn của hệ điều hành, muốn tối phải tự viết cửa sổ dialog riêng.

### 5.2 Các thành phần UI

- **`App.xaml`**: ngoài phần merge theme ở trên còn khai báo converter `StatusTextConverter`. `Controls.xaml` **retemplate hoàn toàn** các control mà WPF vốn vẽ theo Aero2 (kiểu Windows 7) và sẽ lệch hẳn với phần còn lại:
  - `OptionBox` (`ComboBox`): border bo góc 6px, chevron vẽ bằng `Path`, viền chuyển sang màu accent khi hover / focus / đang mở; popup là border bo góc có đổ bóng, `MinWidth` bám theo bề rộng của combobox.
  - `OptionBoxItem` (`ComboBoxItem`): nền bo góc, `IsHighlighted` → xanh nhạt, `IsSelected` → xanh đậm hơn.
  - `ScrollBar`: thanh mảnh 11px, không có nút mũi tên, thumb bo góc đổi màu theo hover/kéo. Hai `ControlTemplate` riêng cho dọc/ngang, chọn qua trigger `Orientation`.
  - `FlatProgressBar`: track + indicator bo góc, bỏ lớp gradient bóng của Aero2 (vốn trông sai hẳn trên nền tối). Trạng thái `IsIndeterminate` (lúc đang quét) dùng một storyboard nhấp nháy opacity, vì template tự viết không có sẵn animation như template mặc định.
  - `TaskRow` (`ListViewItem`): template chứa `GridViewRowPresenter` (bắt buộc để `GridView` còn chia cột đúng), nền hover/selected phẳng thay cho gradient mặc định.
- **`Converters/`**: `StatusBrushConverter` (tô màu cột Status: xanh = Done, đỏ = Failed/Cancelled, cam = Skipped, xanh dương = đang chạy, xám = chờ) và `StatusTextConverter` (đổi tên enum thành chữ người dùng đọc được: `ReadingMetadata` → "Reading").
- **`MainWindow.xaml`** (single-page, chia thành các "card"):
  - Card thư mục input: textbox + combobox **lịch sử thư mục gần đây** (tối đa 8, mới nhất trước, hiển thị full path với tooltip, `TextTrimming` khi quá dài) + `Browse…` (`OpenFolderDialog`, mở sẵn ở thư mục đang chọn) + `Open output` (mở `<input>/output` trong Explorer). Chọn một mục trong lịch sử sẽ gán `InputFolder` và tự chạy `ScanCommand` ngay (giống hành vi kéo-thả).
  - **Kéo–thả**: `Window.AllowDrop = true`, xử lý ở tầng `Preview*` để thắng handler drop mặc định của `TextBox`. Thả **thư mục** → dùng luôn; thả **file** → dùng thư mục chứa nó; thả thứ khác → `DragDropEffects.None`. Khi kéo qua cửa sổ hiện overlay `DropOverlay` viền accent. Thả xong tự chạy `ScanCommand` (thả một thư mục vốn có nghĩa là "xem trong đó có gì").
  - **Empty state**: khi `Tasks.Count == 0`, giữa vùng danh sách hiện icon thư mục + "No videos in the list yet" + gợi ý kéo–thả, thay vì một ô trắng trống trơn.
  - **Đóng cửa sổ giữa lúc đang chạy**: `MainWindow_Closing` là `async void`; nếu `IsBusy` thì đặt `e.Cancel = true` **ngay lập tức** (bắt buộc phải làm trước `await` đầu tiên, khi event còn đang được xử lý), hỏi `IUserPrompt.ConfirmExitWhileBusy`, rồi `await CancelAndWaitAsync` trước khi gọi `Close()` lại với cờ `_isClosingConfirmed`. Trong lúc chờ thì `IsEnabled = false` để người dùng không bấm thêm được gì.
    - Trước khi có bước này, bấm X giữa chừng sẽ **bỏ lại `ffmpeg.exe` chạy mồ côi và một file `.processing.mp4` dở dang** — vì `Closing` chỉ lưu settings, còn `Dispose()` chỉ `Dispose()` cái `CancellationTokenSource` chứ **không `Cancel()`** nó.
    - Giới hạn còn lại: kill bằng Task Manager / `Stop-Process -Force` thì không thể chặn được, vẫn sẽ để lại rác. Đó là bản chất của việc bị kill cứng.
  - Card tuỳ chọn: **7 combobox, nhãn tiếng Việt** — Bố cục, Tốc độ nén, Bộ mã hoá, Chọn video đi kèm, Sau khi ghép xong, Nếu file đã tồn tại, Số luồng CPU. Mỗi combobox bind qua `SelectableOption<T>` (`DisplayMemberPath="Label"`, `SelectedValuePath="Value"`) nên UI hiển thị nhãn thân thiện thay vì tên enum thô như `RandomWithoutImmediateRepeat`. Mỗi ô đều có tooltip tiếng Việt giải thích tác dụng.
    - Chuỗi lựa chọn nằm trong `MainViewModel` (các list `MergeLayouts`, `X264Presets`, …), **không nằm trong XAML** — muốn đổi chữ thì sửa ở đó.
    - `ComboBox` của WPF tự co giãn theo **lựa chọn dài nhất trong danh sách**, nên chữ tiếng Việt dài hơn tiếng Anh sẽ đẩy `WrapPanel` xuống hàng thứ hai. Vì vậy vài nhãn được rút gọn ("Trung bình (nhẹ nhất)", "Ngẫu nhiên, không lặp", "Chuyển vào \"processed\"") và bề rộng cửa sổ mặc định tăng lên 1180 để nhóm ô đầu nằm gọn một hàng. Từ khi có ô "Bộ mã hoá" (7 combobox), `WrapPanel` xuống hàng thứ hai ở độ rộng mặc định — đúng theo thiết kế chịu wrap sẵn có, không phải lỗi; thu nhỏ/phóng to cửa sổ không vỡ layout.
    - Riêng `"processed"` giữ nguyên tiếng Anh vì đó là **tên thư mục thật trên đĩa**, dịch đi sẽ gây hiểu nhầm.
  - Hàng thao tác: Scan / Start / Cancel + progress bar tổng (indeterminate khi đang quét) + phần trăm.
  - `ListView` từng task: File (tên file, tooltip là đường dẫn đầy đủ) / Status (có màu) / Progress (bar + %) / Details. Bật `VirtualizingPanel.VirtualizationMode="Recycling"` + `ScrollUnit="Pixel"` để danh sách hàng nghìn dòng vẫn mượt.
  - `GridView` **không hỗ trợ star sizing**, nên `TaskList_SizeChanged` trong code-behind chia phần bề rộng còn lại (sau khi trừ hai cột cố định Status + Progress và chỗ cho scrollbar) cho hai cột chữ theo tỉ lệ 55/45, có sàn tối thiểu. Nhờ vậy phóng to cửa sổ không còn để thừa khoảng trắng bên phải.
  - Footer: dòng tóm tắt, dòng đếm trực tiếp (`x/y done · ok · failed · skipped`) và thời gian đã trôi qua + ước lượng còn lại.
  - Combobox "Bộ mã hoá" chọn giữa `Cpu` (libx264, mặc định, chạy được trên mọi máy) và `NvidiaGpu` (h264_nvenc) — tooltip nói rõ GPU cần máy có card NVIDIA + driver hỗ trợ, nếu không video sẽ báo `Failed`. App **không tự dò phần cứng**; chọn sai chỉ gây lỗi cho từng task chứ không crash app, vì lỗi đi qua đúng đường xử lý lỗi FFmpeg sẵn có (xem mục 4, `FFmpegCommandBuilder`).
- **`MainViewModel`** (CommunityToolkit.Mvvm, dùng `[ObservableProperty]` / `[RelayCommand]`):
  - `ScanCommand`: gọi `IInputVideoScanner.ScanAsync` trực tiếp (không qua coordinator), dựng **một `ObservableCollection` mới** rồi gán vào `Tasks` — thay vì `Clear()` + `Add()` từng phần tử, vốn phát sinh một sự kiện collection-changed cho mỗi file. Sau khi scan thành công (không ném exception, tức thư mục hợp lệ), gọi `RememberFolder` để đưa thư mục đó lên đầu `RecentFolders` (không phân biệt hoa/thường, bỏ trùng, giữ tối đa 8 mục).
  - `StartCommand`: gọi `IVideoProcessingCoordinator.ProcessAsync` với `ProcessingOptions` build từ **cả 7** tuỳ chọn trên UI (`MergeLayout`, `X264Preset`, `VideoEncoder`, `CpuThreadLimit`, `PairingMode`, `SourceFileAction`, `ExistingOutputAction`).
    - **Cổng chặn thao tác phá huỷ**: nếu `SourceFileAction == Delete`, trước khi làm bất cứ việc gì sẽ gọi `IUserPrompt.ConfirmSourceDeletion(số video, thư mục)` — dialog cảnh báo nêu rõ số file, đường dẫn, và việc file **không vào Recycle Bin**, nút mặc định là **No**. Từ chối → thoát ngay, không đụng gì. Đặt cổng ở thời điểm Start (chứ không phải lúc chọn combobox) vì đó mới là lúc hậu quả thực sự xảy ra.
    - `IUserPrompt` là interface trong `App/Services` (cài đặt `MessageBoxUserPrompt`), để ViewModel không tự mở dialog — giữ đúng nguyên tắc ViewModel không chứa code UI/filesystem và vẫn test được.
  - `CancelCommand`: huỷ cả tiến trình xử lý lẫn lần quét đang chạy.
  - `CancelAndWaitAsync(timeout)`: huỷ **và chờ** lần chạy tháo gỡ xong (await `StartCommand.ExecutionTask` / `ScanCommand.ExecutionTask` của CommunityToolkit), để nơi gọi biết chắc cây tiến trình FFmpeg đã bị kill và file tạm đã được xoá. Có timeout 10 giây để một tiến trình con bị treo không làm cửa sổ không đóng được.
  - Progress: `IProgress<VideoMergeTask>` cập nhật `VideoTaskItemViewModel` tương ứng (map theo `InputFile`). `OverallProgress` được cộng dồn **O(1)** qua một accumulator, không tính lại `Average()` trên toàn bộ danh sách ở mỗi tick FFmpeg; số lượng theo trạng thái cũng được đếm tăng/giảm thay vì quét lại danh sách.
  - Task đã kết thúc (Completed/Skipped/Failed/Cancelled) tính là `EffectiveProgress = 1`, nên một batch có file bị skip vẫn chạy được tới 100%.
  - Khi `InputFolder` đổi sau khi đã quét → danh sách `Tasks` bị xoá và yêu cầu quét lại, tránh chạy batch dựa trên danh sách không còn đúng với thư mục.
  - `CanScan`/`CanStart`/`CanCancel` kiểm soát enable/disable nút dựa trên `IsBusy` và trạng thái dữ liệu.

## 6. Đóng gói & phân phối (portable folder deployment)

Xem chi tiết đầy đủ tại `docs/tasks/Task 9 Convert VideoMergeTool to Portable Folder Deployment.md`. Tóm tắt:

- Ứng dụng được publish dạng **self-contained, folder-based** (không single-file, không trim, không AOT, không cần cài .NET/FFmpeg trên máy người dùng).
- `VideoMergeTool.App.csproj`: khi `Configuration = Release` → `RuntimeIdentifier=win-x64`, `SelfContained=true`, `PublishSingleFile=false`, `PublishTrimmed=false`, `PublishReadyToRun=false`, `DebugType=none`.
- Các thư mục `Tools/`, `Assets/CompanionVideos/`, `Licenses/`, `README.txt` được copy vào output/publish qua `<None Update=... CopyToOutputDirectory=PreserveNewest CopyToPublishDirectory=PreserveNewest>`.
- Script `scripts/publish-portable.ps1` (tham số `-Version`, `-Runtime`, mặc định `1.0.0` / `win-x64`): restore → build Release → test → `dotnet publish` self-contained → verify các file bắt buộc (`VideoMergeTool.exe`, `Tools/ffmpeg.exe`, `Tools/ffprobe.exe`, `Assets/CompanionVideos`, `Licenses`, `README.txt`, ≥1 companion mp4) → nén thành `artifacts/publish/VideoMergeTool-<version>-<runtime>.zip` (ZIP chứa đúng một thư mục gốc `VideoMergeTool-<version>-<runtime>/`).
- `README.txt` (tiếng Việt) hướng dẫn: giải nén → mở `VideoMergeTool.exe` → chọn thư mục → Quét video → Bắt đầu → kết quả nằm trong `output`. Nhắc không xoá `Tools/`, `Assets/`, không tách rời `VideoMergeTool.exe` khỏi thư mục ứng dụng.
- Companion video, `Tools/ffmpeg.exe`, `Tools/ffprobe.exe` không bao giờ bị xoá/ghi đè/di chuyển bởi logic ứng dụng — chỉ được **đọc**.

## 7. Test

- `tests/VideoMergeTool.Core.Tests`: `UnitTest1.cs` (placeholder mặc định, chưa có test thực).
- `tests/VideoMergeTool.Infrastructure.Tests` (33 test):
  - `ApplicationPathValidatorTests.cs` — validate thiếu ffmpeg/ffprobe/companion dir/companion mp4, dùng thư mục tạm, không cần binary FFmpeg thật.
  - `FFprobeServiceTests.cs` — test `FFprobeService.ParseMetadata` (static) với JSON mẫu, không cần chạy FFprobe thật.
  - `InputVideoScannerTests.cs` — test loại trừ thư mục `output`/`processed`, loại trừ file `.processing.mp4`, và báo lỗi khi thư mục input không tồn tại.
  - `OutputPathServiceTests.cs` — test build path output/temp/processed.
  - `FFmpegCommandBuilderTests.cs` — test argument list sinh ra theo từng `ProcessingOptions` (layout, x264 preset, thread limit, `-pix_fmt`, và từ chối kích thước lẻ).
  - `CompanionVideoProviderTests.cs` — thứ tự ổn định, cache danh sách, trả về rỗng khi thiếu thư mục.
  - `UserSettingsServiceTests.cs` — round-trip đủ 7 field, đọc được định dạng enum dạng số của bản cũ, và `Save` không ném khi không ghi được.
  - **`VideoProcessingServiceTests.cs`** — phủ các ràng buộc an toàn cốt lõi bằng fake service + thư mục tạm: commit output rồi mới xử lý input; FFmpeg fail hoặc validate fail thì **giữ nguyên input** và xoá file tạm; xoá input thất bại vẫn là `Completed` kèm cảnh báo; `Skip` không chạy FFmpeg; `Sequential` xoay vòng đúng; `RandomWithoutImmediateRepeat` không lặp liên tiếp; `MoveToProcessed` đặt file đúng chỗ.
  - `VideoMergeTool.Infrastructure.csproj` khai báo `InternalsVisibleTo` cho project test để inject được `Random` có seed vào `VideoProcessingService`.

## 8. Những điểm cần nhớ khi sửa code (rút gọn từ `AGENTS.md`)

- Không bao giờ xoá/di chuyển video input trừ khi: FFmpeg exit thành công **và** file `.processing.mp4` tồn tại **và** FFprobe validate output pass (có video stream, duration lệch ≤ tolerance) **và** file tạm đã được đổi tên (commit) thành file output cuối.
- Khi cancel hoặc fail: giữ nguyên input, xoá file `.processing.mp4`.
- Không bao giờ sửa file trong `Assets/CompanionVideos`.
- Mọi đường dẫn tài nguyên đóng gói phải resolve từ `AppContext.BaseDirectory`, không dùng current working directory.
- Giữ nguyên logic filter FFmpeg, kích thước output, lựa chọn encoder, cách parse progress, quy tắc quét input, an toàn xoá file nguồn, quy tắc validate output, logic chọn companion, thứ tự xử lý task — trừ khi task yêu cầu thay đổi rõ ràng.
- Chạy build và test liên quan trước khi coi task hoàn thành.
