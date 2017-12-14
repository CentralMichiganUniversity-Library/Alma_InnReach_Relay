$body = Get-Content .\LookupUser.xml
$relayAddress = 'http://localhost:59886/NCIPRelay'

Invoke-WebRequest -Uri $relayAddress -Method Post -Body $body | Select-Object -ExpandProperty Content
