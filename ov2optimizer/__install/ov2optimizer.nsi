; NSIS Installer script for ov2optimizer

;Include Modern UI
!include "MUI.nsh"

;for InstallLib
!include "Library.nsh"

;for if/else
!include "LogicLib.nsh"

Var ALREADY_INSTALLED

!define MUI_ICON "icons\SETUP1.ICO"
!define MUI_UNICON "icons\classic-uninstall.ico"

!define MUI_COMPONENTSPAGE_NODESC

;-=-=-=-=-=-=-=-=-=-=-=-=-=-=-
;Name and file
Name "ov2optimizer"
OutFile "ov2optimizer.setup.2.0.0.0.exe"
;-=-=-=-=-=-=-=-=-=-=-=-=-=-=-

;Default installation folder
InstallDir "$PROGRAMFILES\ov2optimizer"

;Get installation folder from registry if available
InstallDirRegKey HKLM "Software\gordthompson.com\ov2optimizer" "InstallPath"

;--------------------------------
;Interface Settings

  !define MUI_ABORTWARNING

;--------------------------------
;Pages

  !insertmacro MUI_PAGE_LICENSE "License.rtf"
; !insertmacro MUI_PAGE_COMPONENTS
  !insertmacro MUI_PAGE_DIRECTORY
  !insertmacro MUI_PAGE_INSTFILES
  
  !insertmacro MUI_UNPAGE_CONFIRM
  !insertmacro MUI_UNPAGE_INSTFILES
  
  ShowInstDetails hide
  ShowUninstDetails hide
  
;--------------------------------
;Languages
 
  !insertmacro MUI_LANGUAGE "English"

;--------------------------------
;Installer Section(s)

Section "Main Section" SecMain

	SetOutPath "$INSTDIR"
  
	IfFileExists "$INSTDIR\ov2optimizer.exe" 0 new_installation
		StrCpy $ALREADY_INSTALLED 1
	new_installation:

	File /oname=ov2optimizer.exe \
		"C:\Users\Gord\Documents\Visual Studio 2010\Projects\ov2optimizer\ov2optimizer\bin\Debug\ov2optimizer.exe"
	File /oname=System.Data.SQLite.dll \
		"C:\Users\Gord\Documents\Visual Studio 2010\Projects\ov2optimizer\ov2optimizer\bin\Debug\System.Data.SQLite.dll"
	File /oname=License.rtf \
		"License.rtf"
		
	;Store installation folder
	WriteRegStr HKLM "Software\gordthompson.com\ov2optimizer" "InstallPath" $INSTDIR
  
	;Create uninstaller
	WriteUninstaller "$INSTDIR\Uninstall.exe"
	
	;register uninstaller with Add/Remove programs
	WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\ov2optimizer" \
   	"DisplayName" "ov2optimizer"
	WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\ov2optimizer" \
   	"UninstallString" "$INSTDIR\Uninstall.exe"
	WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\ov2optimizer" \
   	"NoModify" 1
	WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\ov2optimizer" \
   	"NoRepair" 1

	SetShellVarContext all
	CreateDirectory $STARTMENU\Programs\ov2optimizer
	CreateShortCut "$STARTMENU\Programs\ov2optimizer\ov2optimizer.lnk" "$INSTDIR\ov2optimizer.exe"
	CreateShortCut "$STARTMENU\Programs\ov2optimizer\Uninstall ov2optimizer.lnk" "$INSTDIR\Uninstall.exe"
	
SectionEnd

;--------------------------------
;Uninstaller Section

Section "Uninstall"

	Delete "$INSTDIR\ov2optimizer.exe"
	Delete "$INSTDIR\System.Data.SQLite.dll"
	Delete "$INSTDIR\License.rtf"
	Delete "$INSTDIR\Uninstall.exe"
	
	RMDir "$INSTDIR"
	
	SetShellVarContext all
	Delete "$STARTMENU\Programs\ov2optimizer\ov2optimizer.lnk"
	Delete "$STARTMENU\Programs\ov2optimizer\Uninstall ov2optimizer.lnk"
	RMDir "$STARTMENU\Programs\ov2optimizer"

	DeleteRegValue HKLM "Software\gordthompson.com\ov2optimizer" "InstallPath"
	DeleteRegKey /ifempty HKLM "Software\gordthompson.com\ov2optimizer"
	
	DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\ov2optimizer"

SectionEnd
