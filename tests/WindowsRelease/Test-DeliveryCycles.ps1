$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot DeliveryCycles.ps1)

function Assert-Equal($Actual, $Expected) {
    if ($Actual -cne $Expected) { throw "Expected [$Expected], got [$Actual]." }
}

foreach ($scenario in 'clean','safely recovered','unrecoverable held button') {
    $state = @{Attempts=0; Cycles=0; Failures=0; Passed=$false; Completed=$false; StopReason=$null}
    $fixture = @{LeftDown=$false; InputAllowed=$true; FailureRecorded=$false}
    $events = New-Object 'System.Collections.Generic.List[string]'
    $caught = $null
    try {
        Invoke-DeliveryCycles -State $state -RunCycle {
            if (!$fixture.InputAllowed) { throw 'New input was attempted before recovery.' }
            $events.Add("cycle $($state.Attempts)")
            if ($scenario -ne 'clean' -and $state.Attempts -eq 2) {
                $fixture.LeftDown = $true
                $fixture.InputAllowed = $false
                throw 'Drop missed its deadline.'
            }
        } -RecordFailure {
            param($failure)
            Assert-Equal $failure.Exception.Message 'Drop missed its deadline.'
            $events.Add('failure recorded')
            $fixture.FailureRecorded = $true
        } -Recover {
            Assert-Equal $fixture.FailureRecorded $true
            $events.Add('recovery attempted')
            if ($scenario -eq 'safely recovered') { $fixture.LeftDown = $false }
            if ($fixture.LeftDown) { throw 'Windows still reports a held mouse button.' }
            $fixture.InputAllowed = $true
        } -RecordProgress {} -HasTime { $state.Attempts -lt 4 }
    } catch { $caught = $_.Exception.Message }

    switch ($scenario) {
        'clean' {
            Assert-Equal $caught $null
            Assert-Equal $state.Passed $true
            Assert-Equal $state.Cycles 4
            Assert-Equal $state.Failures 0
        }
        'safely recovered' {
            Assert-Equal $caught $null
            Assert-Equal ($events -join ',') 'cycle 1,cycle 2,failure recorded,recovery attempted,cycle 3,cycle 4'
            Assert-Equal $state.Completed $true
            Assert-Equal $state.Passed $false
            Assert-Equal $state.Cycles 3
            Assert-Equal $state.Failures 1
        }
        'unrecoverable held button' {
            Assert-Equal $caught 'Windows still reports a held mouse button.'
            Assert-Equal $state.StopReason $caught
            Assert-Equal ($events -join ',') 'cycle 1,cycle 2,failure recorded,recovery attempted'
            Assert-Equal $state.Completed $false
            Assert-Equal $state.Passed $false
            Assert-Equal $state.Cycles 1
            Assert-Equal $state.Failures 1
        }
    }
    Write-Output "Passed: $scenario"
}
