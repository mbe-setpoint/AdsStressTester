# Prerequsites for running the testprogram using MQTT

## PLC-program
The plc-program used for testing can be found here:
https://github.com/mbe-setpoint/ADSTestTwinCAT

## Setup a MQTT-broker
For the testing Mosquitto MQTT-broker has been used and set up using this tutorial
https://cedalo.com/blog/how-to-install-mosquitto-mqtt-broker-on-windows/

## Rout to broker in StaticRoutes.xml on PLC
The file StaticRoutes.xml (C:\TwinCAT\3.1\Target\StaticRoutes.xml) must be edited to set a route from the PLC to the MQTT-broker

Add this in <RemoteConnections> tag:

<Json>
    <Name>MQTTConnection</Name>
    <Address>localhost</Address>
    <Topic>p7s</Topic>
</Json>

Documentation:
https://infosys.beckhoff.com/english.php?content=../content/1033/tf6020_tc3_json_data_interface/10821785483.html&id=

## Topic structure
The topics must be according to the documentation from Beckhoff:
https://infosys.beckhoff.com/english.php?content=../content/1033/tf6020_tc3_json_data_interface/10821785483.html&id=

## Webinar from Beckhoff describing this functionality
https://www.youtube.com/watch?v=7NjoDieYFE0

# Reqreating the behaviour 

It is not required to run the .net code to see that the JSON Data Interface results in leakage of ADS router memory
Using a MQTT-test client (like MQTT.fx or MQTT-explorer (http://mqtt-explorer.com/), it's clearly visible that the Router memory declines by sending a request with a json-payload like this:
[{"symbol":"counters.counter01.Value"},{"symbol":"counters.counter02.Value"},{"symbol":"counters.counter03.Value"},{"symbol":"counters.counter04.Value"},{"symbol":"counters.counter05.Value"},{"symbol":"counters.counter06.Value"},{"symbol":"counters.counter07.Value"},{"symbol":"counters.counter08.Value"},{"symbol":"counters.counter09.Value"},{"symbol":"counters.counter10.Value"},{"symbol":"counters.counter11.Value"},{"symbol":"counters.counter12.Value"},{"symbol":"counters.counter13.Value"},{"symbol":"counters.counter14.Value"},{"symbol":"counters.counter15.Value"},{"symbol":"counters.counter16.Value"},{"symbol":"counters.counter17.Value"},{"symbol":"counters.counter18.Value"},{"symbol":"counters.counter19.Value"},{"symbol":"counters.counter20.Value"},{"symbol":"counters.numbers"}]

A video demonstrating the behaviour can be found here:
https://drive.google.com/file/d/1pBGPoc40YO6WM-7M1MNMvRZ-i7vsFJ-P/view?usp=sharing
