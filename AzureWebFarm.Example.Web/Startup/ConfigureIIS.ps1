$ErrorActionPreference = "Stop"
 
try
{
	Add-WindowsFeature Web-Mgmt-Service
	Set-ItemProperty -Path HKLM:\SOFTWARE\Microsoft\WebManagement\Server -Name EnableRemoteManagement -Value 1
	Set-Service -name WMSVC -StartupType Automatic

	Webpicmd.exe /Install /Products:WDeployPS /log:webpi.log /AcceptEula
	Set-Service -name MSDEPSVC -StartupType Automatic

	Start-service WMSVC
	Start-service MSDEPSVC
}
catch
{
	$Host.UI.WriteErrorLine($_)
	exit 1
}
