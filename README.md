NATP
=============
NATP is a tool for nat punchthrough.
It contains two parts, first is turn client (for nat punchthrough), second is signaling client (Room system).

- Easy to modifiy, if you have a socket server & client (UDP, TCP, SSL...), you can easily put NATP into your project.

Before Start
-------------
- server with public IP address (AWS, GCP)
- Install turn on the server, recommand [coturn](https://github.com/coturn/coturn "coturn")
- [Option] install NATP Signaling Server on the server

Examples
-------------
Unity - Mirro NATPTransport
Api Compatibility Level => .NET4.x
...

Referrence
-------------
[OpenP2P](https://github.com/joetex/OpenP2P)

[NetCoreServer](https://github.com/chronoxor/NetCoreServer)

[Mirror](https://github.com/vis2k/Mirror)

