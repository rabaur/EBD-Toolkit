# Using GitHub Desktop

## 1. Download and install GitHub Desktop
Download GitHub Desktop from the [official website](https://github.com/apps/desktop). Follow the installation instructions for your operating system (Windows or macOS).

## 2. Clone the EBD-toolkit repository via GitHub Desktop
The repository (terminology for "project" or "shared folder" in GitHub Desktop) that we need to clone can be found at the front page of the EBD-toolkit GitHub repository: https://github.com/rabaur/EBD-Toolkit. Find the link by clicking the green “Code” button, then copy URL to clipboard. A simplified tutorial can also be found on this page.

![Image](https://github.com/user-attachments/assets/aab64f9b-94d0-4026-a4ff-95741fc380e1)

Open Github Desktop and select File > Clone Repository. In the dialog that opens, select the URL tab and paste the URL you copied from the EBD-toolkit repository. Select a local path where you want to save the repository and click Clone. 

![Image](https://github.com/user-attachments/assets/d8e0a49f-b08a-4f6a-bbf0-62d20a1f1c84)

![Image](https://github.com/user-attachments/assets/52373f9d-8f2f-4f72-adb1-456f46504c32)

>[!IMPORTANT]
> Make sure that the path is not located within any cloud storage folder, such as OneDrive, Google Drive, or Dropbox. This can cause issues with file syncing and version control.

Sometimes, there might be functional updates to the repository. The functionality to check the existence of updates is called "fetching". Upon opening the repository, GitHub Desktop will automatically "fetch" once to check for updates. You can also do this manually by clicking on the "Fetch origin" button in the top bar. Fetching will not change any files in your local repository. 

![Image](https://github.com/user-attachments/assets/1b0f31eb-c1c0-4b73-a037-1e14c55e4ef9)

After fetching, there might be two different scenarios:
1. **No updates available**: If there are no updates, you will see a message indicating that your branch is up to date with the remote repository.
2. **Updates available**: If there are updates, the "Fetch origin" button will change to "Pull origin". Click on this button to download the updates to your local repository. Pull means "to get the latest changes of the software, compare to my current version, and update my version if necessary". After pulling, remote updates are downloaded to your local computer, unless the user has also done local changes to the same files that were updated. 

You might also see a "current branch" dropdown menu in the top bar. For most users, this will be set to "main". This is the default branch of the repository, and the functionalities in this branch are the most stable. Other branches might contain work-in-progress features or experimental changes, and might even be broken. If you are not sure which branch to use, stick to the "main" branch.