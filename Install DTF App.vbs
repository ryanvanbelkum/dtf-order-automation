Set oShell = CreateObject("WScript.Shell")
Set oFSO   = CreateObject("Scripting.FileSystemObject")

' Get the folder this VBS lives in
Dim scriptDir
scriptDir = oFSO.GetParentFolderName(WScript.ScriptFullName)

' Friendly welcome message
MsgBox "Welcome to DTF Order Automation!" & vbCrLf & vbCrLf & _
       "Setup will now install the app. This takes about 2-3 minutes." & vbCrLf & _
       "Click OK to begin.", _
       vbInformation + vbOKOnly, "DTF Order Automation Setup"

' Run the bat file hidden (windowstyle 0 = hidden)
Dim batPath
batPath = """" & scriptDir & "\Install DTF App.bat"""
oShell.Run "cmd /c " & batPath, 0, True

' Done message
MsgBox "Installation complete!" & vbCrLf & vbCrLf & _
       "Open 'DTF Order Automation' from your Desktop." & vbCrLf & _
       "Go to Settings to enter your Shopify and folder details.", _
       vbInformation + vbOKOnly, "DTF Order Automation Setup"
