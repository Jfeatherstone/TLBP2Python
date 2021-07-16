
# Various serial numbers and things
from Utils import timeout
import Settings

def fullInitialization():
    """
    Determine if the TLBP2 device is available and ready to be used
    """

    print('='*50)
    print(' '*15 + 'TLBP2 INITIALIZATION')
    print('='*50)
    
    print("Howdy!\n")
    print("This procedure will ensure that the Thorlabs Beam Profiler is ready to be used with this library" +

    print("\nThe library will require that you have .NETCore installed, as it runs a C# server in the" + 
            "\nbackground to control the beam profiler:")
    print(' '*5 + "https://dotnet.microsoft.com/download/dotnet/3.1")
    
    while True:
        try:

            print("Attempting to connect to the beam profiler...", end='')
            import TLBP2Control

            bpDevice = TLBP2Control.TLBP2()

            status = bpDevice.connect()

            # Measure just to make sure it doesn't throw an error
            measure = bpDevice.getMeasurement()

            if status == 0:
                print('connection successful!')
            else:
                raise Exception()

            bpDevice.disconnect()

            break

        except:
            print('connection failed!')

            print('\nSince the beam profiler could not be found, please follow these diagnostic steps:')
            print(' '*5 + '1. Make sure that the beam profiler is plugged into the computer')
            print(' '*5 + '2. Make sure that the beam profiler is detected by the manufacturer software:')
            print(' '*10 + 'https://www.thorlabs.com/software_pages/ViewSoftwarePage.cfm?Code=Beam')

            print('\nOnce you have gone through these steps, you may type \'retry\' to attempt connection again' +
                    '\nor \'exit\' to exit the initialization procedure.')

            print('>>> ', end='')
            key = input()

            if key == 'retry':
                continue
            elif key == 'exit':
                return

    print('Initialization complete!')

# So that the file can be run from the command line
if __name__ == "__main__":
    fullInitialization()

