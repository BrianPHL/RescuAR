# Railway Deployment Platform — Team Access Guide

This guide explains how team members can access, monitor, and manage the RescuAR Navigation Service deployment on the Railway platform.

---

## 1. Getting Access to the Project

To allow your team members to view and manage the deployment, they need to be invited to the Railway project.

**As the Project Owner:**
1. Log in to your [Railway Dashboard](https://railway.app/dashboard).
2. Open the **RescuAR** (or `chic-abundance`) project.
3. Click on the **Settings** gear icon in the top navigation bar of the project.
4. Go to the **Members** tab.
5. Enter your team member's email address or Railway username and click **Invite**.

**As a Team Member:**
1. You will receive an invitation email.
2. Click the link to accept the invite (you will be prompted to create a Railway account using GitHub, Discord, or Email if you don't have one).
3. Once logged in, you will see the project on your dashboard.

---

## 2. Exposing the Service (Getting a Public URL)

Right now, the service might be marked as **"Unexposed service"**. To make it accessible over the internet:

1. Click on the **perceptive-spontaneity** (Navigation) service block on the project canvas.
2. Go to the **Settings** tab on the right sidebar.
3. Scroll down to the **Networking** section.
4. Click **Generate Domain**. Railway will automatically generate a public `*.up.railway.app` URL for the service.
5. *(Optional)* You can also click **Custom Domain** if you want to use your own domain (e.g., `nav.rescuar.com`).

---

## 3. Monitoring and Troubleshooting

Team members can use the Railway dashboard to monitor the service's health and view logs if something goes wrong.

### Viewing Logs
1. Click on the service block in the project canvas.
2. Select the **Deployments** tab.
3. Click **View logs** on the active (green) deployment.
4. Here you can see two types of logs:
   - **Deploy Logs:** Shows runtime logs (e.g., OSRM startup messages, incoming API requests, or crash errors like `Killed`).
   - **Build Logs:** Shows the Docker build process. Useful if a deployment fails before it even goes online.

### Viewing Metrics
1. Click on the service block.
2. Go to the **Metrics** tab.
3. Here you can monitor **CPU**, **Memory**, and **Network** usage. 
   - *Note: Railway's free tier is limited to 500MB of RAM. If you see the memory usage flatline near 500MB before a crash, it's an Out-Of-Memory (OOM) error.*

---

## 4. Managing Deployments

Railway automatically deploys the service whenever a new commit is pushed to the `dev` branch. However, you can also manage deployments manually:

- **Restart a service:** If the service is hung or crashed, click the three dots (`⋮`) next to the active deployment and select **Restart**.
- **Rollback:** If a new deployment breaks the service, you can find a previous successful deployment in the History list, click the three dots (`⋮`), and select **Redeploy**.
- **Trigger a manual deployment:** You can manually trigger a deployment from the latest commit without pushing new code by clicking **Deploy** in the sidebar.

---

## 5. Environment Variables

If you ever need to change configurations (e.g., adding API keys in the future):
1. Click the service block.
2. Go to the **Variables** tab.
3. Add a **New Variable** (e.g., `PORT=8080`). Railway will automatically restart the service with the new variables applied.
