// WeChatHook.cpp
#include "framework.h"

#pragma data_seg(".SHARED")
HHOOK g_hHook = nullptr;
HWND  g_hReceiver = nullptr;
UINT  g_uFilterMsg = WM_COPYDATA;
#pragma data_seg()
#pragma comment(linker, "/SECTION:.SHARED,RWS")

HINSTANCE g_hInstance = nullptr;

LRESULT CALLBACK CallWndProc(int nCode, WPARAM wParam, LPARAM lParam)
{
    if (nCode >= 0 && lParam != 0)
    {
        const CWPSTRUCT* p = reinterpret_cast<CWPSTRUCT*>(lParam);

        if (p->message == g_uFilterMsg)
        {
            if (g_hReceiver && IsWindow(g_hReceiver))
            {
                SendMessageW(
                    g_hReceiver,
                    p->message,
                    p->wParam,
                    p->lParam
                );
            }
        }
    }

    return CallNextHookEx(g_hHook, nCode, wParam, lParam);
}

extern "C" __declspec(dllexport)
void UninstallHook()
{
    if (g_hHook)
    {
        UnhookWindowsHookEx(g_hHook);
        g_hHook = nullptr;
    }
}

extern "C" __declspec(dllexport)
BOOL InstallHook(HWND hReceiver, DWORD dwThreadId, UINT uFilterMsg)
{
    if (g_hHook != nullptr)
        return TRUE;

    g_hReceiver = hReceiver;
    g_uFilterMsg = (uFilterMsg == 0) ? WM_COPYDATA : uFilterMsg;

    g_hHook = SetWindowsHookExW(
        WH_CALLWNDPROC,
        CallWndProc,
        g_hInstance,
        dwThreadId
    );

    return g_hHook != nullptr;
}

BOOL APIENTRY DllMain(HMODULE hModule,
    DWORD  ul_reason_for_call,
    LPVOID lpReserved)
{
    if (ul_reason_for_call == DLL_PROCESS_ATTACH)
    {
        g_hInstance = hModule;
        DisableThreadLibraryCalls(hModule);
    }
    return TRUE;
}