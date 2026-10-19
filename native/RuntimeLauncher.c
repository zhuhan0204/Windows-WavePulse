#define UNICODE
#define _UNICODE

#include <windows.h>
#include <windowsx.h>
#include <shellapi.h>
#include <shlobj.h>
#include <wincrypt.h>
#include <string.h>
#include <wchar.h>
#include "PayloadHash.h"

#define ID_APP_ICON 1
#define IDC_CLOSE_BUTTON 1001
#define IDC_RECHECK_BUTTON 1002
#define TIMER_CLOSE_AFTER_PASS 2001
#define NET48_MIN_RELEASE 528040
#define ID_CORE_PAYLOAD 101
#define ID_CONFIG_PAYLOAD 102

static const wchar_t *APP_TITLE = L"WavePulse 无线设备电量助手";
static const wchar_t *CORE_EXE = L"WavePulse 无线设备电量助手.exe";
static const wchar_t *CORE_CONFIG = L"WavePulse 无线设备电量助手.exe.config";
static const wchar_t *DOWNLOAD_URL = L"https://dotnet.microsoft.com/zh-cn/download/dotnet-framework/net48";

static HINSTANCE g_instance;
static HFONT g_title_font;
static HFONT g_header_font;
static HFONT g_body_font;
static HFONT g_small_font;
static HFONT g_link_font;
static HICON g_icon;
static UINT g_dpi = 96;
static RECT g_link_rect;
static BOOL g_preview_mode = FALSE;
static BOOL g_check_passed = FALSE;

static int ScaleValue(int value)
{
    return MulDiv(value, (int)g_dpi, 96);
}

static RECT ScaleRect(int left, int top, int right, int bottom)
{
    RECT rect = {
        ScaleValue(left),
        ScaleValue(top),
        ScaleValue(right),
        ScaleValue(bottom)
    };
    return rect;
}

static HFONT CreateUiFont(int point_size, int weight, BOOL underline)
{
    HDC screen = GetDC(NULL);
    int height = -MulDiv(point_size, GetDeviceCaps(screen, LOGPIXELSY), 72);
    ReleaseDC(NULL, screen);
    return CreateFontW(
        height, 0, 0, 0, weight, FALSE, underline, FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
        CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_DONTCARE, L"Microsoft YaHei UI");
}

static BOOL ReadFrameworkRelease(REGSAM view, DWORD *release)
{
    HKEY key = NULL;
    LONG result = RegOpenKeyExW(
        HKEY_LOCAL_MACHINE,
        L"SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full",
        0,
        KEY_QUERY_VALUE | view,
        &key);
    if (result != ERROR_SUCCESS) return FALSE;

    DWORD value = 0;
    DWORD type = 0;
    DWORD size = sizeof(value);
    result = RegQueryValueExW(key, L"Release", NULL, &type, (LPBYTE)&value, &size);
    RegCloseKey(key);
    if (result != ERROR_SUCCESS || type != REG_DWORD) return FALSE;
    *release = value;
    return TRUE;
}

static BOOL IsNet48Installed(void)
{
    DWORD release = 0;
    if (ReadFrameworkRelease(KEY_WOW64_64KEY, &release) && release >= NET48_MIN_RELEASE) return TRUE;
    release = 0;
    if (ReadFrameworkRelease(KEY_WOW64_32KEY, &release) && release >= NET48_MIN_RELEASE) return TRUE;
    return FALSE;
}

static BOOL GetApplicationDirectory(wchar_t *directory, DWORD capacity)
{
    DWORD length = GetModuleFileNameW(NULL, directory, capacity);
    if (length == 0 || length >= capacity) return FALSE;
    wchar_t *separator = wcsrchr(directory, L'\\');
    if (separator == NULL) return FALSE;
    *separator = L'\0';
    return TRUE;
}

static BOOL GetEmbeddedPayload(int resource_id, const BYTE **data, DWORD *size)
{
    HRSRC resource = FindResourceW(g_instance, MAKEINTRESOURCEW(resource_id), RT_RCDATA);
    if (resource == NULL) return FALSE;
    HGLOBAL loaded = LoadResource(g_instance, resource);
    if (loaded == NULL) return FALSE;
    *size = SizeofResource(g_instance, resource);
    *data = (const BYTE *)LockResource(loaded);
    return *data != NULL && *size > 0;
}

static BOOL CalculateSha256(const BYTE *data, DWORD size, BYTE output[32])
{
    HCRYPTPROV provider = 0;
    HCRYPTHASH hash = 0;
    BOOL success = FALSE;
    DWORD output_size = 32;
    if (!CryptAcquireContextW(&provider, NULL, NULL, PROV_RSA_AES, CRYPT_VERIFYCONTEXT)) goto cleanup;
    if (!CryptCreateHash(provider, CALG_SHA_256, 0, 0, &hash)) goto cleanup;
    if (!CryptHashData(hash, data, size, 0)) goto cleanup;
    if (!CryptGetHashParam(hash, HP_HASHVAL, output, &output_size, 0) || output_size != 32) goto cleanup;
    success = TRUE;
cleanup:
    if (hash != 0) CryptDestroyHash(hash);
    if (provider != 0) CryptReleaseContext(provider, 0);
    return success;
}

static BOOL VerifyBytes(const BYTE *data, DWORD size)
{
    BYTE hash[32];
    return CalculateSha256(data, size, hash) && memcmp(hash, EXPECTED_CORE_SHA256, 32) == 0;
}

static BOOL VerifyFile(const wchar_t *path)
{
    HANDLE file = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) return FALSE;
    DWORD size = GetFileSize(file, NULL);
    if (size == INVALID_FILE_SIZE || size == 0)
    {
        CloseHandle(file);
        return FALSE;
    }
    BYTE *buffer = (BYTE *)HeapAlloc(GetProcessHeap(), 0, size);
    if (buffer == NULL)
    {
        CloseHandle(file);
        return FALSE;
    }
    DWORD read = 0;
    BOOL success = ReadFile(file, buffer, size, &read, NULL) && read == size && VerifyBytes(buffer, size);
    HeapFree(GetProcessHeap(), 0, buffer);
    CloseHandle(file);
    return success;
}

static BOOL WritePayload(const wchar_t *path, const BYTE *data, DWORD size)
{
    HANDLE file = CreateFileW(path, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_HIDDEN, NULL);
    if (file == INVALID_HANDLE_VALUE) return FALSE;
    DWORD written = 0;
    BOOL success = WriteFile(file, data, size, &written, NULL) && written == size;
    FlushFileBuffers(file);
    CloseHandle(file);
    return success;
}

static BOOL CreateRuntimeDirectoryUnder(
    const wchar_t *root,
    const wchar_t *product_folder,
    wchar_t *directory,
    DWORD capacity)
{
    wchar_t product_root[MAX_PATH];
    wchar_t runtime_root[MAX_PATH];
    if (_snwprintf(product_root, MAX_PATH - 1, L"%ls\\%ls", root, product_folder) < 0) return FALSE;
    product_root[MAX_PATH - 1] = L'\0';
    if (_snwprintf(runtime_root, MAX_PATH - 1, L"%ls\\Runtime", product_root) < 0) return FALSE;
    runtime_root[MAX_PATH - 1] = L'\0';
    if (_snwprintf(directory, capacity - 1, L"%ls\\v0.1", runtime_root) < 0) return FALSE;
    directory[capacity - 1] = L'\0';
    CreateDirectoryW(product_root, NULL);
    SetFileAttributesW(product_root, FILE_ATTRIBUTE_HIDDEN);
    CreateDirectoryW(runtime_root, NULL);
    CreateDirectoryW(directory, NULL);
    DWORD attributes = GetFileAttributesW(directory);
    return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
}

static BOOL GetRuntimeDirectory(wchar_t *directory, DWORD capacity)
{
    wchar_t launcher_directory[MAX_PATH];
    if (GetApplicationDirectory(launcher_directory, MAX_PATH) &&
        CreateRuntimeDirectoryUnder(launcher_directory, L".wavepulse-runtime", directory, capacity))
    {
        return TRUE;
    }

    wchar_t local_app_data[MAX_PATH];
    DWORD local_length = GetEnvironmentVariableW(L"LOCALAPPDATA", local_app_data, MAX_PATH);
    if (local_length == 0 || local_length >= MAX_PATH)
    {
        if (SHGetFolderPathW(NULL, CSIDL_LOCAL_APPDATA | CSIDL_FLAG_CREATE, NULL, SHGFP_TYPE_CURRENT, local_app_data) != S_OK)
        {
            return FALSE;
        }
    }
    return CreateRuntimeDirectoryUnder(local_app_data, L"WavePulse", directory, capacity);
}

static BOOL PrepareManagedApplication(HWND owner, wchar_t *executable, DWORD executable_capacity, wchar_t *runtime_directory, DWORD directory_capacity)
{
    const BYTE *payload = NULL;
    const BYTE *configuration = NULL;
    DWORD payload_size = 0;
    DWORD configuration_size = 0;
    if (!GetEmbeddedPayload(ID_CORE_PAYLOAD, &payload, &payload_size) ||
        !GetEmbeddedPayload(ID_CONFIG_PAYLOAD, &configuration, &configuration_size))
    {
        MessageBoxW(owner, L"应用文件不完整，缺少内置主程序。请重新下载官方发布文件。", APP_TITLE, MB_OK | MB_ICONERROR);
        return FALSE;
    }
    if (!VerifyBytes(payload, payload_size))
    {
        MessageBoxW(owner, L"应用完整性校验失败，文件可能被修改或重新打包。请仅使用 yuban 发布的原始版本。", APP_TITLE, MB_OK | MB_ICONERROR);
        return FALSE;
    }
    if (!GetRuntimeDirectory(runtime_directory, directory_capacity))
    {
        MessageBoxW(owner, L"无法创建 WavePulse 本地运行目录。", APP_TITLE, MB_OK | MB_ICONERROR);
        return FALSE;
    }

    wchar_t configuration_path[MAX_PATH];
    if (_snwprintf(executable, executable_capacity - 1, L"%ls\\%ls", runtime_directory, CORE_EXE) < 0) return FALSE;
    executable[executable_capacity - 1] = L'\0';
    if (_snwprintf(configuration_path, MAX_PATH - 1, L"%ls\\%ls", runtime_directory, CORE_CONFIG) < 0) return FALSE;
    configuration_path[MAX_PATH - 1] = L'\0';

    if (!VerifyFile(executable) && !WritePayload(executable, payload, payload_size))
    {
        MessageBoxW(owner, L"无法释放 WavePulse 主程序。请关闭正在运行的旧版本后重试。", APP_TITLE, MB_OK | MB_ICONERROR);
        return FALSE;
    }
    if (!VerifyFile(executable))
    {
        MessageBoxW(owner, L"本地主程序完整性校验失败，已阻止启动。", APP_TITLE, MB_OK | MB_ICONERROR);
        return FALSE;
    }
    if (!WritePayload(configuration_path, configuration, configuration_size))
    {
        MessageBoxW(owner, L"无法写入 WavePulse 运行配置。", APP_TITLE, MB_OK | MB_ICONERROR);
        return FALSE;
    }
    return TRUE;
}

static BOOL LaunchManagedApplication(HWND owner)
{
    wchar_t directory[MAX_PATH];
    wchar_t runtime_directory[MAX_PATH];
    wchar_t executable[MAX_PATH];
    wchar_t command_line[MAX_PATH * 2];
    if (!GetApplicationDirectory(directory, MAX_PATH)) return FALSE;
    if (!PrepareManagedApplication(owner, executable, MAX_PATH, runtime_directory, MAX_PATH)) return FALSE;
    wchar_t launcher_path[MAX_PATH];
    if (GetModuleFileNameW(NULL, launcher_path, MAX_PATH) == 0) return FALSE;
    SetEnvironmentVariableW(L"WAVEPULSE_LAUNCHER_PATH", launcher_path);
    SetEnvironmentVariableW(L"WAVEPULSE_PORTABLE_DIR", directory);
    BOOL start_in_background = wcsstr(GetCommandLineW(), L"--background") != NULL;
    if (_snwprintf(
        command_line,
        (MAX_PATH * 2) - 1,
        L"\"%ls\"%ls",
        executable,
        start_in_background ? L" --background" : L"") < 0) return FALSE;
    command_line[(MAX_PATH * 2) - 1] = L'\0';

    STARTUPINFOW startup;
    PROCESS_INFORMATION process;
    ZeroMemory(&startup, sizeof(startup));
    ZeroMemory(&process, sizeof(process));
    startup.cb = sizeof(startup);
    if (!CreateProcessW(executable, command_line, NULL, NULL, FALSE, 0, NULL, runtime_directory, &startup, &process))
    {
        wchar_t message[256];
        _snwprintf(message, 255, L"无法启动 WavePulse 主程序。Windows 错误代码：%lu", GetLastError());
        message[255] = L'\0';
        MessageBoxW(owner, message, APP_TITLE, MB_OK | MB_ICONERROR);
        return FALSE;
    }
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return TRUE;
}

static void FillRoundedRect(HDC dc, const RECT *rect, int radius, COLORREF color)
{
    HBRUSH brush = CreateSolidBrush(color);
    HPEN pen = CreatePen(PS_SOLID, 1, color);
    HGDIOBJ old_brush = SelectObject(dc, brush);
    HGDIOBJ old_pen = SelectObject(dc, pen);
    RoundRect(dc, rect->left, rect->top, rect->right, rect->bottom, radius, radius);
    SelectObject(dc, old_brush);
    SelectObject(dc, old_pen);
    DeleteObject(brush);
    DeleteObject(pen);
}

static void DrawTextInRect(HDC dc, const wchar_t *text, RECT rect, HFONT font, COLORREF color, UINT format)
{
    HGDIOBJ old_font = SelectObject(dc, font);
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, color);
    DrawTextW(dc, text, -1, &rect, format);
    SelectObject(dc, old_font);
}

static void PaintWindow(HWND window)
{
    PAINTSTRUCT paint;
    HDC dc = BeginPaint(window, &paint);
    RECT client;
    GetClientRect(window, &client);

    HBRUSH background = CreateSolidBrush(RGB(245, 248, 252));
    FillRect(dc, &client, background);
    DeleteObject(background);

    RECT header = { 0, 0, client.right, ScaleValue(104) };
    HBRUSH header_brush = CreateSolidBrush(RGB(13, 48, 99));
    FillRect(dc, &header, header_brush);
    DeleteObject(header_brush);

    if (g_icon != NULL) DrawIconEx(dc, ScaleValue(35), ScaleValue(24), g_icon, ScaleValue(56), ScaleValue(56), 0, NULL, DI_NORMAL);
    RECT header_title = ScaleRect(108, 23, 480, 56);
    DrawTextInRect(dc, L"运行环境检测", header_title, g_header_font, RGB(255, 255, 255), DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    RECT header_subtitle = ScaleRect(108, 58, 500, 83);
    DrawTextInRect(dc, L"WavePulse 启动前安全检查", header_subtitle, g_small_font, RGB(176, 210, 255), DT_LEFT | DT_VCENTER | DT_SINGLELINE);

    if (g_preview_mode)
    {
        RECT preview = ScaleRect(494, 34, 582, 67);
        FillRoundedRect(dc, &preview, ScaleValue(16), RGB(41, 105, 181));
        DrawTextInRect(dc, L"样式预览", preview, g_small_font, RGB(230, 243, 255), DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    }

    RECT title_rect = ScaleRect(40, 132, 580, 163);
    DrawTextInRect(dc, g_check_passed ? L"运行环境检测通过" : L"需要安装 .NET Framework 4.8", title_rect, g_title_font, RGB(24, 38, 58), DT_LEFT | DT_VCENTER | DT_SINGLELINE);

    RECT description = ScaleRect(40, 174, 580, 220);
    const wchar_t *description_text = g_preview_mode
        ? L"这是缺少运行环境时显示的提示样式。实际启动时，安装完成后无需关闭窗口，点击“重新检测”即可继续进入应用。"
        : L"WavePulse 需要此 Windows 组件才能运行。安装完成后无需关闭本窗口，点击“重新检测”即可继续进入应用。";
    DrawTextInRect(dc, description_text, description, g_body_font, RGB(82, 99, 122), DT_LEFT | DT_WORDBREAK);

    RECT status_rect = ScaleRect(40, 230, 580, 270);
    COLORREF status_background = g_check_passed ? RGB(229, 248, 238) : RGB(255, 240, 238);
    COLORREF status_foreground = g_check_passed ? RGB(21, 137, 78) : RGB(211, 69, 57);
    FillRoundedRect(dc, &status_rect, ScaleValue(18), status_background);
    RECT status_text = ScaleRect(56, 230, 565, 270);
    DrawTextInRect(
        dc,
        g_check_passed ? L"✓ 已检测到 .NET Framework 4.8 或更高版本" : L"! 尚未检测到 .NET Framework 4.8 或更高版本",
        status_text,
        g_body_font,
        status_foreground,
        DT_LEFT | DT_VCENTER | DT_SINGLELINE);

    DrawTextInRect(dc, L"前往微软官方下载 .NET Framework 4.8  →", g_link_rect, g_link_font, RGB(29, 108, 218), DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    RECT safe_note = ScaleRect(40, 312, 340, 334);
    DrawTextInRect(dc, L"下载链接将使用系统默认浏览器打开。", safe_note, g_small_font, RGB(128, 143, 163), DT_LEFT | DT_VCENTER | DT_SINGLELINE);

    EndPaint(window, &paint);
}

static void DrawButton(const DRAWITEMSTRUCT *item)
{
    BOOL primary = item->CtlID == IDC_RECHECK_BUTTON;
    BOOL pressed = (item->itemState & ODS_SELECTED) != 0;
    COLORREF fill = primary
        ? (pressed ? RGB(20, 91, 187) : RGB(29, 111, 229))
        : (pressed ? RGB(232, 238, 246) : RGB(255, 255, 255));
    COLORREF border = primary ? fill : RGB(206, 218, 233);
    COLORREF text = primary ? RGB(255, 255, 255) : RGB(51, 68, 90);

    HBRUSH brush = CreateSolidBrush(fill);
    HPEN pen = CreatePen(PS_SOLID, 1, border);
    HGDIOBJ old_brush = SelectObject(item->hDC, brush);
    HGDIOBJ old_pen = SelectObject(item->hDC, pen);
    RoundRect(item->hDC, item->rcItem.left, item->rcItem.top, item->rcItem.right, item->rcItem.bottom, ScaleValue(14), ScaleValue(14));
    SelectObject(item->hDC, old_brush);
    SelectObject(item->hDC, old_pen);
    DeleteObject(brush);
    DeleteObject(pen);

    wchar_t label[32];
    GetWindowTextW(item->hwndItem, label, 32);
    RECT text_rect = item->rcItem;
    DrawTextInRect(item->hDC, label, text_rect, g_body_font, text, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    if ((item->itemState & ODS_FOCUS) != 0)
    {
        RECT focus = item->rcItem;
        InflateRect(&focus, -ScaleValue(4), -ScaleValue(4));
        DrawFocusRect(item->hDC, &focus);
    }
}

static LRESULT CALLBACK RuntimeWindowProc(HWND window, UINT message, WPARAM w_param, LPARAM l_param)
{
    switch (message)
    {
        case WM_PAINT:
            PaintWindow(window);
            return 0;

        case WM_ERASEBKGND:
            return 1;

        case WM_DRAWITEM:
            DrawButton((const DRAWITEMSTRUCT *)l_param);
            return TRUE;

        case WM_COMMAND:
            if (LOWORD(w_param) == IDC_CLOSE_BUTTON)
            {
                DestroyWindow(window);
                return 0;
            }
            if (LOWORD(w_param) == IDC_RECHECK_BUTTON)
            {
                if (IsNet48Installed())
                {
                    if (g_preview_mode)
                    {
                        g_check_passed = TRUE;
                        InvalidateRect(window, NULL, TRUE);
                        SetTimer(window, TIMER_CLOSE_AFTER_PASS, 1300, NULL);
                    }
                    else if (LaunchManagedApplication(window))
                    {
                        DestroyWindow(window);
                    }
                }
                else
                {
                    g_check_passed = FALSE;
                    MessageBeep(MB_ICONWARNING);
                    InvalidateRect(window, NULL, TRUE);
                }
                return 0;
            }
            break;

        case WM_LBUTTONUP:
        {
            POINT point = { GET_X_LPARAM(l_param), GET_Y_LPARAM(l_param) };
            if (PtInRect(&g_link_rect, point))
            {
                ShellExecuteW(window, L"open", DOWNLOAD_URL, NULL, NULL, SW_SHOWNORMAL);
                return 0;
            }
            break;
        }

        case WM_SETCURSOR:
        {
            POINT point;
            GetCursorPos(&point);
            ScreenToClient(window, &point);
            if (PtInRect(&g_link_rect, point))
            {
                SetCursor(LoadCursorW(NULL, IDC_HAND));
                return TRUE;
            }
            break;
        }

        case WM_TIMER:
            if (w_param == TIMER_CLOSE_AFTER_PASS)
            {
                KillTimer(window, TIMER_CLOSE_AFTER_PASS);
                DestroyWindow(window);
                return 0;
            }
            break;

        case WM_CLOSE:
            DestroyWindow(window);
            return 0;

        case WM_DESTROY:
            PostQuitMessage(0);
            return 0;
    }
    return DefWindowProcW(window, message, w_param, l_param);
}

static int ShowRuntimeDialog(void)
{
    const wchar_t *window_class = L"WavePulseRuntimeCheckWindow";
    WNDCLASSEXW window_class_info;
    ZeroMemory(&window_class_info, sizeof(window_class_info));
    window_class_info.cbSize = sizeof(window_class_info);
    window_class_info.style = CS_HREDRAW | CS_VREDRAW;
    window_class_info.lpfnWndProc = RuntimeWindowProc;
    window_class_info.hInstance = g_instance;
    window_class_info.hIcon = g_icon;
    window_class_info.hIconSm = g_icon;
    window_class_info.hCursor = LoadCursorW(NULL, IDC_ARROW);
    window_class_info.hbrBackground = NULL;
    window_class_info.lpszClassName = window_class;
    if (!RegisterClassExW(&window_class_info)) return 1;

    DWORD style = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU;
    RECT requested = { 0, 0, ScaleValue(620), ScaleValue(410) };
    AdjustWindowRect(&requested, style, FALSE);
    int width = requested.right - requested.left;
    int height = requested.bottom - requested.top;
    RECT work_area;
    SystemParametersInfoW(SPI_GETWORKAREA, 0, &work_area, 0);
    int left = work_area.left + ((work_area.right - work_area.left - width) / 2);
    int top = work_area.top + ((work_area.bottom - work_area.top - height) / 2);

    HWND window = CreateWindowExW(
        WS_EX_APPWINDOW,
        window_class,
        L"运行环境检测 - WavePulse 无线设备电量助手",
        style,
        left, top, width, height,
        NULL, NULL, g_instance, NULL);
    if (window == NULL) return 1;

    HWND close_button = CreateWindowExW(
        0, L"BUTTON", L"关闭",
        WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_OWNERDRAW,
        ScaleValue(356), ScaleValue(344), ScaleValue(104), ScaleValue(42),
        window, (HMENU)(INT_PTR)IDC_CLOSE_BUTTON, g_instance, NULL);
    HWND recheck_button = CreateWindowExW(
        0, L"BUTTON", L"重新检测",
        WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_OWNERDRAW,
        ScaleValue(476), ScaleValue(344), ScaleValue(104), ScaleValue(42),
        window, (HMENU)(INT_PTR)IDC_RECHECK_BUTTON, g_instance, NULL);
    SendMessageW(close_button, WM_SETFONT, (WPARAM)g_body_font, TRUE);
    SendMessageW(recheck_button, WM_SETFONT, (WPARAM)g_body_font, TRUE);

    {
        typedef HRESULT (WINAPI *DwmSetWindowAttributeFunction)(HWND, DWORD, LPCVOID, DWORD);
        HMODULE dwm_api = LoadLibraryW(L"dwmapi.dll");
        if (dwm_api != NULL)
        {
            DwmSetWindowAttributeFunction set_attribute =
                (DwmSetWindowAttributeFunction)GetProcAddress(dwm_api, "DwmSetWindowAttribute");
            if (set_attribute != NULL)
            {
                const DWORD corner_preference_attribute = 33;
                const DWORD rounded_corner_preference = 2;
                set_attribute(window, corner_preference_attribute, &rounded_corner_preference, sizeof(rounded_corner_preference));
            }
            FreeLibrary(dwm_api);
        }
    }

    ShowWindow(window, SW_SHOW);
    UpdateWindow(window);
    SetForegroundWindow(window);

    MSG message;
    while (GetMessageW(&message, NULL, 0, 0) > 0)
    {
        if (!IsDialogMessageW(window, &message))
        {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
    }
    return (int)message.wParam;
}

int WINAPI WinMain(HINSTANCE instance, HINSTANCE previous_instance, LPSTR command_line, int show_command)
{
    (void)previous_instance;
    (void)show_command;
    g_instance = instance;
    (void)command_line;
    {
        typedef UINT (WINAPI *GetDpiForSystemFunction)(void);
        HMODULE user_api = GetModuleHandleW(L"user32.dll");
        if (user_api != NULL)
        {
            GetDpiForSystemFunction get_dpi =
                (GetDpiForSystemFunction)GetProcAddress(user_api, "GetDpiForSystem");
            if (get_dpi != NULL)
            {
                UINT detected_dpi = get_dpi();
                if (detected_dpi >= 96 && detected_dpi <= 480) g_dpi = detected_dpi;
            }
        }
    }
    g_link_rect = ScaleRect(40, 286, 390, 312);
    g_preview_mode = wcsstr(GetCommandLineW(), L"--preview-runtime-dialog") != NULL;
    g_icon = LoadIconW(instance, MAKEINTRESOURCEW(ID_APP_ICON));
    g_title_font = CreateUiFont(18, FW_BOLD, FALSE);
    g_header_font = CreateUiFont(19, FW_BOLD, FALSE);
    g_body_font = CreateUiFont(11, FW_NORMAL, FALSE);
    g_small_font = CreateUiFont(9, FW_NORMAL, FALSE);
    g_link_font = CreateUiFont(10, FW_SEMIBOLD, TRUE);

    int result;
    if (!g_preview_mode && IsNet48Installed())
    {
        result = LaunchManagedApplication(NULL) ? 0 : 1;
    }
    else
    {
        result = ShowRuntimeDialog();
    }

    if (g_title_font != NULL) DeleteObject(g_title_font);
    if (g_header_font != NULL) DeleteObject(g_header_font);
    if (g_body_font != NULL) DeleteObject(g_body_font);
    if (g_small_font != NULL) DeleteObject(g_small_font);
    if (g_link_font != NULL) DeleteObject(g_link_font);
    if (g_icon != NULL) DestroyIcon(g_icon);
    return result;
}
