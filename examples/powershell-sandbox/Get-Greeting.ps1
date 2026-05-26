[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateNotNullOrEmpty()]
    [string]$Name = 'AgentBridge'
)

Set-StrictMode -Version Latest

"Hello, $Name!"
