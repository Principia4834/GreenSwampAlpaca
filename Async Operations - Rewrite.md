**Async Operations - Rewrite**

The Green Swamp Alpaca program, must support ASCOM ITelescopeV4 asynchronous operations. The specification is here [ITelescope V4 Interface — ASCOM Master Interfaces 1.0.17 documentation](https://ascom-standards.org/newdocs/telescope.html)

Look carefully at FindHome, Park, SlewToTargetAsync, SlewToTargetAsync and SlewToAltAzAsync. Each method has a completion property that is monitored by a client app to determine when the operation has completed.

The existing code base uses code in Telescope.cs to handle ASCOM requests which are executed by code in the SkyServer static class. This code is spread across SkyServer.cs, SkyServer.Core.cs and SkyServer.TelescopeAPI.cs

There is an async method - GoToAsync which all ASCOM methods ultimately call to execute the telescope movement. This method is poorly structured and probably has race condition faults and logic errors.

Critical requirements:

1. A telescope movement must be cancellable by AbortSlew or by the receipt of another telescope movement command from the ASCOM interface
2. The telescope movement implementation must be thread safe

GoToAsync probably needs restructuring into three sections

1. Setup - checks state, set another local variables, initiates movement and signals back that the Telescope ASCOM call can return - must be less than one second
2. Movement - controls telescope movement using SimGoTo or SkyGoTo functions until slewing has completed or failed. It must be possible to cleanly cancel movement at any time
3. Completion - tracking is enabled as required and slewing properties are set correctly so ASCOM completion properties get the correct value

The cancellation requirement should use task cancellation tokens.

It may be necessary to apply locking to some or all of the AsyncGoTo method.

The AsyncGoTo method need be protected from re-entrancy issues