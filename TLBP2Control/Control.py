import subprocess
from .Server import *
import os
from time import sleep

import numpy as np

import multiprocessing.pool
import functools

# Could possibly change if you mess around with directory structure
CS_SERVER_EXE = r'CSServer\TLBP2PipeConnection.exe'
LAUNCH_ARGS = ' --suppress-output' # To make the output from the server not be projected into the python output


# Current file location (where this script is)
CURR_FILE_DIR =  os.path.dirname(__file__)

# You should almost never change this, as it is built into the 
# server executable above, and would require changing the C# source
PIPE_NAME = 'TLBP2PyConnection'

def timeout(s):
    """
    Decorator function to timeout a process after s seconds
    """
    def timeout_decorator(item):
        @functools.wraps(item)
        def func_wrapper(*args, **kwargs):
            pool = multiprocessing.pool.ThreadPool(processes=1)
            async_result = pool.apply_async(item, args, kwargs)
            # Raise a timeout error if it takes too long
            return async_result.get(s)
        return func_wrapper
    return timeout_decorator


class TLBP2():
  
    # Messages that can be sent through the pipe
    _STATUS = 'status'
    _MEASURE = 'measure'
    _STOP = 'stop'

    def __init__(self, debug=False):
        """

        """
        self._isConnected = False
        self._serverRunning = False
        self._debugMode = False
        pass

    def connect(self, timeoutDuration=20):
        # First check to see if we need to be connected
        if self._isConnected:
            return

        # Otherwise, we have to do a few things:
        # 1. Start the C# server that actually connects to the TLBP209
        # 2. Establish the connection between the server and python
        # 3. Verify that we are ready to take measurements

        # 1.
        if not self._debugMode:
            self._csProcess = subprocess.Popen(CURR_FILE_DIR + '\\' + CS_SERVER_EXE + LAUNCH_ARGS)
            self._serverRunning = True
        else:
            self._csProcess = None
            self._serverRunning = False

        # 2.
        self._pipeCon = PipeServer(PIPE_NAME)

        @timeout(timeoutDuration)
        def _con():
            self._pipeCon.connect()

        try:
            _con()
        except:
            return 1 

        # 3.
        self._pipeCon.write(self._STATUS)
        status = self._pipeCon.read()

        # First entry is the status of the actual pipe (ie did the message send/receive)
        # and the second is the status of the beam profiler
        # These of course aren't written anywhere in the original code, but from guessing:
        # 3 means the drum speed is stabilized and ready to measure
        # 4 means the drum is just starting up 
        # 5 means the drum is spinning but possibly not stable? (not sure how it differs from 4, but :/)
        if status[0] == 0 and int(status[1].decode()) in [3, 5]:
            self._isConnected = True
            return 0

        # Otherwise, we have an issue
        return 1

    def disconnect(self):
        # First check if we are connected at all
        if not self._serverRunning and not self._isConnected:
            print("Not connected")
            return

        # Otherwise, we have to do a few things:
        # 1. Send the stop signal through the pipe to disable the beam profiler
        # 2. Close the pipe connection itself
        # 3. Stop the server process

        # 1.
        # It's possible if we're in debug mode that the user closed the terminal
        # manually, which would have closed the pipe already
        # So surround this in try just in case
        try:
            self._pipeCon.write(self._STOP)
            response = self._pipeCon.read() # Should be "Stopping" but we don't really care
        except:
            # Though if it happens when not in debug mode, that might be problem
            if not self._debugMode:
                print('Pipe closed unexpectedly!')

        # There can be some issues if we shut down the pipe too soon after send the stop
        # so we hold for a little to ensure that the stop command goes through properly
        sleep(.25)

        # 2.
        self._pipeCon.close()

        # 3. 
        if not self._debugMode:
            self._csProcess.kill()
        
        self._serverRunning = False
        self._isConnected = False
        
        return
 
    def getStatus(self):
        """
        Get the status of the beam profiler control library. Possible states are:

        0: Connected to server and beam profiler; can measure
        1: Connected to server and beam profiler, but beam profiler not ready; cannot measure
        2: Connected to server but not beam profiler; cannot measure
        3: Completely disconnected; cannot measure
        """
        if not self._serverRunning and not self._debugMode and not self._isConnected:
            return 3

        if not self._isConnected:
            return 2

        self._pipeCon.write(self._STATUS)
        status = self._pipeCon.read() # Should be 3

        if not int(status[1].decode()) in [3, 5]:
            return 1

        return 0

    def getMeasurement(self):
        """
        Read out the measurement from the beam profiler. Data is returned in dictionary form,
        including:

        - Centroid position
        - Peak position
        - Peak intensity
        - Drum speed at measurement time
        - Beam width
        - Gaussian fit parameters
            - These are 4 numbers that represent:
            1. Center
            2. Width
            3. Amplitude
            4. Fit Percentage

        """
        # Make sure we actually can take a measurement
        if self.getStatus() != 0:
            return None

        # Grab the raw data
        self._pipeCon.write(self._MEASURE)
        rawData = self._pipeCon.read()[1].decode().strip()

        #print(rawData)

        if rawData == "Error measuring":
            return None

        # Data fields are separated by | character
        fieldsArr = rawData.split('|')
        
        fieldsDict = {}

        for s in fieldsArr:
            key, val = s.split('=')

            if len(val.split(',')) == 1:
                fieldsDict[key] = float(val)
            else:
                fieldsDict[key] = np.array([float(ele) for ele in val.split(',')], dtype='double')

        return fieldsDict

