How to use NATPTransport
-------------
## Server
#### Step1 Setup coturn
##### Install
`$sudo yum install git`

`$sudo yum install emacs-nox`

`$sudo yum install build-essential`

`$sudo yum install libssl-dev sqlite3`

`$sudo yum install libsqlite3-dev`

`$sudo yum install libevent-dev`

`$sudo yum install g++`

`$sudo yum install libboost-dev`

`$sudo yum install libevent-dev`

`$sudo yum install coturn`

##### Config
`$sudo cp /etc/coturn/turnserver.conf /etc/coturn/turnserver.conf.default`

`$sudo rm /etc/coturn/turnserver.conf`

`$sudo nano /etc/coturn/turnserver.conf`
>  listening-port=3478
external-ip=EXTERNAL IP ADDRESS
user=user:password`
cli-password=password123456

#### Step2 Setup signaling server
##### Install .NET Core
`$sudo rpm -Uvh https://packages.microsoft.com/config/centos/7/packages-microsoft-  prod.rpm`

`$sudo yum install dotnet-sdk-3.1`
##### Build 
`$scd /home/user/NATP_SignalingServer`

`$dotnet build`

#### Step3 Run
##### Coturn 
`$sudo turnserver -v -a -f -c /etc/coturn/turnserver.conf -r realm`
##### SignalingServer
`$cd /home/user/NATP_SignalingServer/NATP_SignalingServer/bin/Debug/netcoreapp3.1`
`$sudo ./NATP_SignalingServer`

## Client
1. Import [Mirror](https://github.com/vis2k/Mirror "Mirror")
2. Import [NATPTransport.unitypackage](https://github.com/cxz456999/NATP/tree/master/Release/Unity_Mirror_Transport "NATPTransport.unitypackage")
3. Use NATPTransport as transport
4. Input the public IP address (in coturn config external-ip)
5. Input roomtag
6. enjoyed
