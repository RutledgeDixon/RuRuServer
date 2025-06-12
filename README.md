# RuRuServer
Server side for RuRu
Simple TCP server:
  1. Allows up to two clients at a time
  2. If only one client is logged on, buffers messages and sends them to the next unique client
  3. Encodes messages using RuRu style

# *Installation Instructions:*
  1. Download the release and store the folder in somewhere where you will remember
  2. Allow port-forwarding on your router for the IP address that the server is run off of
  3. Open Task Scheduler and create a new task that runs RuRuCommsServer.exe from the folder you downloaded every time your computer starts up
     1. search for 'Task Scheduler' and run it
     2. click 'Create Task...'
     3. name the task
     4. go to the triggers tab and add an 'at startup' trigger
     5. go to the actions tab, add an action, choose 'Start a program' from the dropdown, hit the browse button, and find RuRuCommsServer.exe
     6. go to the settings tab, and uncheck the box that would stop the task after a certain number of days
