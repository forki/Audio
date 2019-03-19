module Utils
open System.Net.NetworkInformation


let getMACAddress() = 
    NetworkInterface.GetAllNetworkInterfaces()
    |> Seq.filter (fun nic -> 
        nic.OperationalStatus = OperationalStatus.Up &&
        nic.NetworkInterfaceType <> NetworkInterfaceType.Loopback)
    |> Seq.map (fun nic -> nic.GetPhysicalAddress().ToString())
    |> Seq.head
