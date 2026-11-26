# ⚡ WiringSim — Phần mềm Mô phỏng Đấu nối Thiết bị Điện Trực quan

![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11%20(x64)-0078D6?style=flat-square&logo=windows)
![Framework](https://img.shields.io/badge/.NET-8.0%20WPF-512BD4?style=flat-square&logo=dotnet)
![Release](https://img.shields.io/badge/Version-v1.0__Release-2EA043?style=flat-square)
![Language](https://img.shields.io/badge/Language-Tiếng%20Việt%20%7C%20English-orange?style=flat-square)

**WiringSim** là ứng dụng desktop mã nguồn mở trên Windows hỗ trợ học tập, giảng dạy và mô phỏng trực quan quá trình đấu nối mạch điện từ dân dụng đến công nghiệp. Phần mềm cho phép kéo thả khí cụ điện, đi dây theo màu tiêu chuẩn, cấu hình thông số thiết bị và chạy thử nghiệm trạng thái đóng cắt theo thời gian thực.

<!-- Kéo thả ảnh chụp màn hình giao diện thực tế vào đây khi chỉnh sửa trên GitHub -->
<!-- ![WiringSim Interface](docs/screenshot.png) -->

---

## ✨ Tính năng nổi bật (v1.0_Release)

* **Đấu nối trực quan & Đi dây linh hoạt:**
  * Hỗ trợ 5 màu dây tiêu chuẩn kỹ thuật: **Đỏ** (`L1 / DC+`), **Vàng** (`L2`), **Xanh dương** (`L3`), **Đen** (`N / DC-`), **Xanh lá** (`PE`).
  * Hai chế độ vẽ dây: **Vuông góc (90°)** tự động bắt dính lưới hoặc **Đường cong mượt (Smooth Curve)**; cho phép tạo điểm bẻ góc (`Waypoints`), kéo nắn dây tự do và đi dây chéo góc khi giữ phím `Shift`.
  * Hỗ trợ **Xoay (90°)** và **Lật ngang/dọc** mọi thiết bị trên bảng lắp mạch.
* **Cấu hình thông số chuyên sâu qua Menu chuột phải:**
  * Tùy chỉnh thông số kỹ thuật sát với khí cụ thực tế: điện áp cuộn hút (`24VDC`, `24VAC`, `110VAC`, `220VAC`, `380VAC`), cấu hình tiếp điểm `NO/NC`, dòng định mức Aptomat, dải dòng Rơ-le nhiệt, chế độ Timer và ngõ ra `PNP/NPN` của cảm biến.
* **Tích hợp Biến tần (VFD) & Hộp thoại lập trình Keypad:**
  * Mô phỏng mặt điều khiển biến tần trực quan với màn hình LED tần số, phím bấm `RUN / STOP / MODE`, núm xoay chiết áp ảo và bảng cài đặt thông số (`P01`–`P06`: nguồn lệnh chạy, nguồn đặt tần số, tần số tối đa, thời gian tăng tốc `ACC`, giảm tốc `DEC`, chạy đa cấp tốc độ).
* **Mô phỏng trạng thái & Bảo vệ thông minh:**
  * **Live Wire:** Dây dẫn tự động phát sáng khi có dòng điện chạy qua; trục động cơ quay thuận/nghịch và thay đổi tốc độ theo tần số biến tần hoặc kiểu đấu Sao/Tam giác.
  * **Phát hiện sự cố thực tế:** Tự động ngắt Aptomat khi phát hiện ngắn mạch pha-pha hoặc pha-trung tính; cảnh báo quá tải, cháy cuộn hút khi cấp quá điện áp; bắt lỗi đấu nhầm đồng hồ Ampe song song với nguồn.
* **Giao diện hiện đại & Tiện ích:**
  * Thiết kế tối ưu cho Windows 11, hỗ trợ 3 chế độ giao diện **Tối (Dark) / Sáng (Light) / Theo hệ thống**, song ngữ **Tiếng Việt — English**.
  * Tích hợp sẵn bộ **Mạch mẫu**, lưu/mở sơ đồ định dạng `.json`, nhắc lưu trước khi thoát và tự động kiểm tra cập nhật từ GitHub Releases.

---

## 🧰 Thư viện khí cụ & thiết bị hỗ trợ

| Nhóm thiết bị | Danh sách khí cụ & thông số |
| :--- | :--- |
| **Nguồn cấp điện** | Nguồn DC (`5V / 12V / 24V / 48V`), Nguồn AC 1 pha (`220V`), Nguồn AC 3 pha (`380V` — hỗ trợ giả lập mất từng pha `L1, L2, L3`). |
| **Đóng cắt & Điều khiển** | Aptomat MCB `1P / 3P` (`6A–63A`, đặc tuyến `B/C/D`), Công tắc 1 chiều, Công tắc 2 chiều (cầu thang), Công tắc xoay 3 vị trí (`1-0-2`), Nút nhấn $\varnothing 22$ (`NO`, `NC`, kép `NO+NC`, tự nhả hoặc có nhớ), Công tắc hành trình (`WLCA2`). |
| **Rơ-le & Khởi động từ** | Contactor (`LC1D / MC`), Rơ-le trung gian (`MY2N` 8 chân / `MY4N` 14 chân), Rơ-le nhiệt (`LRD / MT` có nút `TEST Trip` & `RESET`), Rơ-le thời gian (`Omron H3CR`: `ON-Delay`, `OFF-Delay`, `Star-Delta`). |
| **Cảm biến công nghiệp** | Cảm biến từ (Inductive), Cảm biến điện dung (Capacitive), Cảm biến quang (Photoelectric), Cảm biến siêu âm/báo mức — hỗ trợ 3 dây/4 dây, chuẩn `PNP / NPN`. |
| **Động cơ & Truyền động** | Động cơ 3 pha 3 chân (chọn đấu Sao/Tam giác nội bộ), Động cơ 3 pha 6 chân (đấu mạch Sao - Tam giác hoặc 2 cấp tốc độ), Biến tần (`VFD / Inverter`). |
| **Tải & Đo lường** | Đèn báo pha, Bóng đèn dân dụng, Đồng hồ Volt (`AC/DC`, Digital/Analog), Đồng hồ Ampe (`AC/DC`), Biến dòng đo lường `CT` (`50/5A` – `200/5A`). |

---

## 🚀 Cài đặt & Sử dụng

### Cách 1: Tải bản phát hành sẵn (Khuyên dùng)
1. Truy cập trang [**Releases**](https://github.com/dangdinhnambhlc2709-alt/WiringSim/releases) của dự án.
2. Tải về một trong hai gói cài đặt ở phiên bản mới nhất (`v1.0_Release`):
   * **`WiringSim_Setup_v1.0.exe`:** Trình cài đặt chuẩn trên Windows (tự động tạo lối tắt trên Desktop và Start Menu).
   * **`WiringSim_v1.0_Release_win-x64.zip`:** Bản chạy độc lập (Portable) — giải nén và chạy trực tiếp tệp `WiringSim.exe` không cần cài đặt.
3. Yêu cầu hệ thống: Windows 10 hoặc Windows 11 (64-bit).

### Cách 2: Tự biên dịch từ mã nguồn
Yêu cầu máy tính đã cài đặt **.NET 8.0 SDK** và **Visual Studio 2022** (hoặc .NET CLI):

```powershell
git clone https://github.com/dangdinhnambhlc2709-alt/WiringSim.git
cd WiringSim
dotnet run
```

Đóng gói thành một tệp `.exe` độc lập duy nhất:
```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish/Standalone
```

---

## ⌨️ Phím tắt & Thao tác nhanh

| Thao tác / Phím tắt | Chức năng |
| :--- | :--- |
| **Kéo thả / Nhấp đúp** thiết bị ở thanh trái | Đưa thiết bị vào bảng lắp mạch |
| **Chuột trái chân A $\rightarrow$ điểm trống $\rightarrow$ chân B** | Vẽ dây dẫn có điểm bẻ góc theo ý muốn |
| **Giữ `Shift` khi kéo dây** | Đi dây chéo góc tự do không bắt dính lưới |
| **Chuột phải vào thiết bị / dây dẫn** | Mở menu cấu hình thông số, đổi nhãn, xoay/lật hoặc đổi màu dây |
| **Phím `R`** | Xoay nhanh thiết bị đang chọn góc 90° |
| **Phím `Delete`** | Xóa thiết bị hoặc dây dẫn đang chọn |
| **Lăn chuột / Kéo nền trống** | Phóng to, thu nhỏ (Zoom) và di chuyển vùng nhìn (Pan) |

---

## 🗺️ Lộ trình phát triển (Roadmap)

* [x] **v1.0_Release:** Hệ thống khí cụ điện dân dụng & công nghiệp cơ bản, cảm biến, thiết bị đo lường, biến tần (VFD), giao diện Sáng/Tối Windows 11 và đa ngôn ngữ Việt/Anh.
* [ ] **v2.0 (Đang phát triển):** Bổ sung khối điều khiển lập trình **PLC S7-1200**, trình soạn thảo Ladder tích hợp và hỗ trợ kết nối mô phỏng trực tiếp với **Siemens TIA Portal (S7-PLCSIM)**.
* [ ] **v2.1:** Bổ sung đồng hồ vạn năng VOM / Ampe kìm cầm tay và tính năng xuất bảng thống kê vật tư (BOM).