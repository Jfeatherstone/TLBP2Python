import win32pipe, win32file

BUFFER_SIZE = 65536

# The server setup to communicate with the measurements done in C#
# The user should not actually use this class, but should interact
# via the wrappers in Control.py
class PipeServer():
    
    def __init__(self, pipeName):
        self.pipeName = pipeName

        self.pipe = win32pipe.CreateNamedPipe(
                r'\\.\pipe\\' + pipeName,
                win32pipe.PIPE_ACCESS_DUPLEX, # Allows both read and write on both sides
                win32pipe.PIPE_TYPE_MESSAGE | win32pipe.PIPE_READMODE_MESSAGE | win32pipe.PIPE_WAIT,
                1, # Only 1 instance allowed at any given time
                BUFFER_SIZE, BUFFER_SIZE , # min and max buffer size (should be the same)
                30, # Long (ish) timeout period
                None) # No security

    # Pretty basic wrappers for basic operations with the pipe
    # Note that this hangs while waiting for a connection
    def connect(self):
        win32pipe.ConnectNamedPipe(self.pipe, None)

    def write(self, message):
        win32file.WriteFile(self.pipe, message.encode() + b'\n')

    # Note that this hangs while waiting for a message, and can lock up the program
    def read(self):
        return win32file.ReadFile(self.pipe, BUFFER_SIZE)

    def close(self):
        win32file.CloseHandle(self.pipe)
