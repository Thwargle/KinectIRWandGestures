# Kinect IR Wand Gestures - Harry Potter Spell Recognition

A Windows application that uses a Microsoft Kinect sensor and an IR-tipped wand to record and recognize Harry Potter spells through gesture recognition. The system tracks wand movements in real-time and matches them against a library of spell templates using the $1 Gesture Recognizer algorithm.

## Overview

This project enables users to cast spells from the Harry Potter universe by drawing gesture patterns in the air with an IR-emitting wand. The Kinect sensor tracks the wand's infrared signature, maps it to the color camera view, and recognizes the drawn patterns as specific spells.

## Features

- **Real-time Wand Tracking**: Tracks an IR-emitting wand tip using the Kinect's infrared sensor
- **Spell Recognition**: Recognizes drawn gestures as Harry Potter spells using the $1 Gesture Recognizer algorithm
- **Spell Recording**: Record custom spell templates by drawing gestures with your wand
- **Template Management**: Save and reload spell templates to/from JSON files
- **Visual Feedback**: 
  - Live color camera feed with wand position overlay (red dot)
  - Real-time drawing of wand path (green line)
  - Spell preview legend showing all available spells
- **Default Spell Library**: Includes 40+ pre-configured Harry Potter spells
- **Automatic Recognition**: Auto-recognizes spells when gestures are completed
- **Stroke Cleanup**: Filters noise and invalid segments from wand tracking data

## Requirements

### Hardware
- **Microsoft Kinect for Windows v2** sensor
- **IR-emitting wand**: A wand or pointer with an infrared LED/emitter at the tip
- Windows 10 or later
- USB 3.0 port (required for Kinect v2)

### Software
- Windows 10 or later
- .NET Framework 4.6.2 or later
- Microsoft Kinect SDK 2.0
- Visual Studio 2017 or later (for building from source)

## Installation

1. Install the [Microsoft Kinect SDK 2.0](https://www.microsoft.com/en-us/download/details.aspx?id=44561)
2. Connect your Kinect sensor to a USB 3.0 port
3. Build the project in Visual Studio
4. Ensure `templates.json` is in the same directory as the executable (created automatically on first run)

## Usage

### Basic Operation

1. **Launch the application** - The Kinect sensor will initialize automatically
2. **Check sensor status** - The sensor indicator (green/red dot) shows connection status
3. **Adjust IR threshold** - Use the slider to fine-tune wand detection sensitivity (default: 50000)
4. **Cast a spell** - Draw a gesture pattern in the air with your IR wand
5. **View recognition** - The spell name appears in the status bar when recognized

### Recording New Spells

1. **Enter spell name** - Type the spell name in the "Spell name" text box
2. **Arm recording** - Click "Arm Record" button (or press Space when armed)
3. **Position wand** - Recenter your wand in the viewport
4. **Start recording** - Press Space or click "Start" button
5. **Draw the gesture** - Draw your spell pattern with the wand
6. **Stop recording** - Press Enter or click "Stop" button
7. **Save templates** - Click "Save Templates" to persist your recordings

### Keyboard Shortcuts

- **Space**: Start recording (when armed) or trigger recognition
- **Enter**: Stop recording (when recording)
- **Esc**: Cancel current recording operation

### Recognition Behavior

The system automatically recognizes spells when:
- You complete a closed shape (returns to start point)
- You hold the wand still for 700ms after drawing
- You lose tracking for 14 consecutive frames
- Maximum stroke duration (2.5 seconds) is reached

Spell recognition fails if:
- Stroke path exceeds 1800 pixels
- Spell takes longer than 3 seconds
- Confidence score is below 0.7 (configurable)

## Technical Details

### Wand Tracking Pipeline

1. **IR Detection**: Finds the brightest spot in the infrared frame (wand tip)
2. **Centroid Calculation**: Computes weighted centroid around the peak IR value
3. **Depth Lookup**: Retrieves depth value at the IR pixel location
4. **Coordinate Mapping**: Maps depth pixel to color camera space using Kinect's coordinate mapper
5. **Canvas Projection**: Projects color coordinates to the display canvas (aspect-corrected)

### Recognition Algorithm

The project uses the **$1 Gesture Recognizer** algorithm with the following features:

- **Normalization**: 
  - Resamples strokes to 96 points
  - Rotates to zero (based on centroid-to-start angle)
  - Scales to 250x250 square
  - Translates to origin

- **Rotation Tolerance**: Searches ±45° rotation range using golden section search
- **Score Calculation**: Distance-based score (0.0-1.0), minimum 0.7 to accept
- **Template Matching**: Compares normalized input against all stored templates

### Stroke Processing

- **Cleanup**: Removes jumps (>60px), gaps (>120ms), and short segments (<120px)
- **Validation**: Requires minimum 10 points and 30 points for recognition/recording
- **Noise Filtering**: Filters out tracking artifacts and invalid depth readings

### Default Spells Included

The application includes 40+ pre-configured spells:

- **Combat**: Expelliarmus, Stupefy, Petrificus Totalus, Confringo, Impedimenta
- **Utility**: Alohomora, Lumos, Nox, Reparo, Revelio, Scourgify
- **Transformation**: Engorgio, Reducio, Wingardium Leviosa
- **Healing**: Episkey, Vulnera Sanentur
- **Charms**: Accio, Aguamenti, Arresto Momentum, Ascendio, Descendo
- **And many more...**

## Project Structure

```
KinectIrWandGestures/
├── MainWindow.xaml          # Main UI layout
├── MainWindow.xaml.cs      # Main application logic, sensor handling
├── OneDollarRecognizer.cs  # $1 Gesture Recognizer implementation
├── TemplateStore.cs        # JSON template persistence
├── StrokeCleanup.cs        # Stroke filtering and cleanup
├── Point2D.cs              # 2D point with timestamp
├── RecognizeResult.cs     # Recognition result data structure
└── DefaultSpellTemplates.cs # Pre-configured spell templates
```

## Configuration

### IR Threshold
Adjust the IR threshold slider to control wand detection sensitivity:
- **Lower values**: More sensitive, may detect ambient IR
- **Higher values**: Less sensitive, requires brighter IR source
- **Default**: 50000 (recommended for most IR LEDs)

### Recognition Settings
Key constants in `MainWindow.xaml.cs`:
- `MinScoreToAccept`: 0.7 (minimum confidence to recognize)
- `MaxStrokeLengthPixels`: 1800 (maximum path length)
- `MaxSpellDuration`: 3.0 seconds
- `StationaryTimeout`: 700ms (hold-to-finish timeout)

### Recording Settings
- `MinRecordPoints`: 30 (minimum points to save)
- `MinRecordDuration`: 400ms (minimum recording time)
- `MissingFramesToEndStroke`: 14 frames (auto-stop on tracking loss)

## Troubleshooting

### Sensor Not Detected
- Ensure Kinect is connected to USB 3.0 port
- Check Kinect SDK 2.0 is installed
- Verify sensor power LED is on
- Try unplugging and reconnecting the sensor

### Wand Not Tracking
- Increase IR threshold if wand is too dim
- Decrease IR threshold if detecting ambient IR
- Ensure wand IR emitter is pointing toward Kinect
- Check for direct sunlight or bright IR sources interfering

### Poor Recognition
- Record your own templates for better accuracy
- Draw spells consistently (same size and orientation)
- Ensure complete gestures (closed shapes work best)
- Check that strokes are long enough (>60px path length)

### Recognition Too Sensitive/Not Sensitive Enough
- Adjust `MinScoreToAccept` in `OneDollarRecognizer.cs` (lower = more sensitive)
- Record multiple templates for the same spell
- Use "Force Recognize" button to test recognition manually

## File Format

Templates are stored in JSON format:

```json
[
  {
    "Name": "Lumos",
    "Points": [
      {"X": 379.38, "Y": 500.94},
      {"X": 378.29, "Y": 500.96},
      ...
    ]
  },
  ...
]
```

## Development

### Building from Source

1. Open `KinectIrWandGestures.sln` in Visual Studio
2. Ensure Kinect SDK 2.0 is installed (sets `KINECTSDK20_DIR` environment variable)
3. Build the solution (F6)
4. Run from Visual Studio or execute `bin/Debug/KinectIrWandGestures.exe`

### Dependencies

- **Microsoft.Kinect.dll**: Kinect SDK 2.0 (referenced from SDK installation)
- **.NET Framework 4.6.2**: WPF application framework
- **System.Runtime.Serialization**: JSON template serialization

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Credits

- Uses the **$1 Gesture Recognizer** algorithm (Wobbrock et al., 2007)
- Built with Microsoft Kinect SDK 2.0
- WPF-based Windows application

## Notes

- The application requires a physical IR-emitting wand (not included)
- Best results achieved in controlled lighting conditions
- Multiple recordings of the same spell improve recognition accuracy
- The system is rotation-tolerant but works best with consistent gesture size

