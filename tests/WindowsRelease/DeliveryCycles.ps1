function Invoke-DeliveryCycles {
    param(
        [hashtable]$State,
        [scriptblock]$RunCycle,
        [scriptblock]$RecordFailure,
        [scriptblock]$Recover,
        [scriptblock]$RecordProgress,
        [scriptblock]$HasTime
    )
    do {
        $State.Attempts++
        try {
            & $RunCycle
            $State.Cycles++
        } catch {
            $failure = $_
            $State.Failures++
            & $RecordFailure $failure
            if (!$Recover) { throw $failure }
            try {
                & $Recover
            } catch {
                $State.StopReason = $_.Exception.Message
                throw
            }
        }
        & $RecordProgress
    } while (& $HasTime)
    $State.Completed = $true
    $State.Passed = $State.Failures -eq 0
}
