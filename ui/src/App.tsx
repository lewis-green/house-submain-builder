import { Route, Routes } from 'react-router'
import { BomPage } from './routes/BomPage'
import { CataloguePage } from './routes/CataloguePage'
import { NewSubmainPage } from './routes/NewSubmainPage'
import { NotFoundPage } from './routes/NotFoundPage'
import { PanelPage } from './routes/PanelPage'
import { ProjectPage } from './routes/ProjectPage'
import { ProjectsPage } from './routes/ProjectsPage'

export function App() {
  return (
    <Routes>
      <Route path="/" element={<ProjectsPage />} />
      <Route path="/catalogue" element={<CataloguePage />} />
      <Route path="/projects/:projectId" element={<ProjectPage />} />
      <Route path="/projects/:projectId/bom" element={<BomPage />} />
      <Route path="/projects/:projectId/submains/new" element={<NewSubmainPage />} />
      <Route path="/submains/:submainId/panel" element={<PanelPage />} />
      <Route path="/submains/:submainId/bom" element={<BomPage />} />
      {/* Without this an unknown address renders nothing at all, which reads as
          a broken app rather than a wrong link. */}
      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  )
}
