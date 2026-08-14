Unicode True
RequestExecutionLevel user
SetCompressor /SOLID lzma

!ifndef APP_VERSION
  !error "APP_VERSION is required"
!endif
!ifndef APP_FILE_VERSION
  !error "APP_FILE_VERSION is required"
!endif
!ifndef APP_ARCH
  !error "APP_ARCH is required"
!endif
!ifndef APP_EXE
  !error "APP_EXE is required"
!endif
!ifndef OUTPUT_FILE
  !error "OUTPUT_FILE is required"
!endif
!ifndef APP_ICON
  !error "APP_ICON is required"
!endif

Name "Dirvexa"
Caption "Dirvexa ${APP_VERSION} (${APP_ARCH})"
OutFile "${OUTPUT_FILE}"
Icon "${APP_ICON}"
UninstallIcon "${APP_ICON}"
InstallDir "$LOCALAPPDATA\Programs\Dirvexa"
InstallDirRegKey HKCU "Software\Fallax Vision\Dirvexa" "InstallLocation"
ShowInstDetails show
ShowUninstDetails show

VIProductVersion "${APP_FILE_VERSION}"
VIAddVersionKey /LANG=1033 "ProductName" "Dirvexa"
VIAddVersionKey /LANG=1033 "CompanyName" "Fallax Vision"
VIAddVersionKey /LANG=1033 "FileDescription" "Dirvexa Setup (${APP_ARCH})"
VIAddVersionKey /LANG=1033 "FileVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=1033 "ProductVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Copyright 2026 Fallax Vision"

Page directory
Page instfiles
UninstPage uninstConfirm
UninstPage instfiles

Section "Install"
  SetOutPath "$INSTDIR"
  File /oname=Dirvexa.exe "${APP_EXE}"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateDirectory "$SMPROGRAMS\Dirvexa"
  CreateShortcut "$SMPROGRAMS\Dirvexa\Dirvexa.lnk" "$INSTDIR\Dirvexa.exe" "" "$INSTDIR\Dirvexa.exe" 0
  CreateShortcut "$SMPROGRAMS\Dirvexa\Uninstall Dirvexa.lnk" "$INSTDIR\Uninstall.exe" "" "$INSTDIR\Uninstall.exe" 0
  CreateShortcut "$DESKTOP\Dirvexa.lnk" "$INSTDIR\Dirvexa.exe" "" "$INSTDIR\Dirvexa.exe" 0

  WriteRegStr HKCU "Software\Fallax Vision\Dirvexa" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Dirvexa" "DisplayName" "Dirvexa"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Dirvexa" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Dirvexa" "Publisher" "Fallax Vision"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Dirvexa" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Dirvexa" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Dirvexa" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Dirvexa" "NoRepair" 1
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\Dirvexa.lnk"
  Delete "$SMPROGRAMS\Dirvexa\Dirvexa.lnk"
  Delete "$SMPROGRAMS\Dirvexa\Uninstall Dirvexa.lnk"
  RMDir "$SMPROGRAMS\Dirvexa"

  Delete "$INSTDIR\Dirvexa.exe"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"

  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Dirvexa"
  DeleteRegKey HKCU "Software\Fallax Vision\Dirvexa"

  MessageBox MB_YESNO|MB_ICONQUESTION|MB_DEFBUTTON2 \
    "Do you also want to delete Dirvexa's saved scan history, settings, backups, and logs?" \
    IDYES DeleteUserData
  Goto KeepUserData
  DeleteUserData:
  RMDir /r "$LOCALAPPDATA\Dirvexa"
  KeepUserData:
SectionEnd
